using Notes.Features.Components.Archive;
using Notes.Features.Components.Download;
using Notes.Features.Helpers;

namespace Notes.Features.ZippedFolders;

/// <summary>What <see cref="FolderArchiver.Recover"/> found a folder to be in.</summary>
public enum ArchivedFolderState
{
    /// <summary>Only the archive: nothing is unzipped.</summary>
    Zipped,

    /// <summary>
    /// The unzipped copy is there, and is what the folder holds now (it may have changes the archive hasn't): it is to be zipped again, not
    /// thrown away.
    /// </summary>
    Unzipped,

    /// <summary>Neither the archive nor an unzipped copy (only ever from outside the app: someone deleted it).</summary>
    Missing
}

/// <summary>
/// Zips a folder's unzipped copy into its archive and unzips it again, so that at no point (a crash, a power cut) is anything lost: there is
/// always either an unzipped copy that holds everything, or an archive that does, and <see cref="Recover"/> tells which from what is on disk.
/// <para>Zipping (<see cref="ZipAsync"/>):</para>
/// <list type="number">
/// <item>The new archive is written next to the old one (<see cref="ArchivedFolder.NewArchivePath"/>) and flushed to the disk, noting the hash
/// of every file as it goes in.</item>
/// <item>It is read back: it must hold exactly those files with those hashes, and the same directories.</item>
/// <item>It takes the old one's place with a rename, which either happens or doesn't.</item>
/// <item>Only then is the unzipped copy renamed into the work directory (it is gone at once) and deleted there.</item>
/// </list>
/// <para>Unzipping (<see cref="UnzipAsync"/>): into the work directory, flushed to the disk, then renamed into place. Until that rename, the
/// archive is what counts.</para>
/// Neither may run twice at once for the same folder, nor while its unzipped copy is changed: that is the caller's to make sure of.
/// </summary>
/// <param name="settings">For <see cref="ArchivesConfig.OverwritePasses"/> (its default without).</param>
public sealed class FolderArchiver(ILogger<FolderArchiver> logger, ArchivesConfig? settings = null)
{
    /// <summary>How often plaintext that goes (an encrypted folder's unzipped copy) is overwritten first.</summary>
    private int OverwritePasses => (settings ?? new ArchivesConfig()).OverwritePasses;

    /// <summary>
    /// As the listing has it: hidden and system entries go in like any other, and a directory that can't be read must fail the zipping
    /// rather than look empty.
    /// </summary>
    private static readonly EnumerationOptions ListOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    /// <summary>Called at each point between the steps (only the tests listen, to stop it there).</summary>
    public Action<ArchiveStep>? StepReached { get; set; }

    // ---- Zipping

    /// <summary>
    /// Zips the unzipped copy into the archive (replacing the old one, if there is one) and deletes the copy. Throws an
    /// <see cref="IOException"/> if it fails: the copy is then still there (and counts), and the archive is the old one, or, if only taking the
    /// copy away failed, the new one, which holds the same.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops it until the new archive takes the old one's place (the copy is then as it was); from there on it is finished either way.
    /// </param>
    /// <param name="progress">Told how much is done (0 to 1) as it goes: writing the archive, then reading it back.</param>
    /// <param name="status">Told what it is doing as it goes (<see cref="WorkStatus"/>), for the text under the bar.</param>
    public async Task ZipAsync(ArchivedFolder folder, ArchiveOptions options, CancellationToken cancellationToken,
        IProgress<double>? progress = null, Action<WorkStatus>? status = null)
    {
        if (!Directory.Exists(folder.UnzippedDirectory))
            throw new DirectoryNotFoundException($"{folder.UnzippedDirectory} is not there to be zipped.");

        DeleteIfThere(folder.NewArchivePath);
        try
        {
            FolderManifest zipped = new();
            int filesOpened = 0;
            void Opened()
            {
                if (++filesOpened == 2)
                    Reached(ArchiveStep.WritingArchive);
            }

            // Writing takes the most of it, reading it back (unpacking and hashing) the rest
            ArchiveProgress? writing = null;
            if (progress is not null || status is not null)
            {
                // (Off the caller's thread: it may be a request, which shouldn't wait for a big folder to be walked)
                status?.Invoke(new WorkStatus(WorkPhases.Measuring));
                (long bytes, int files) = await Task.Run(() => MeasureCopy(folder.UnzippedDirectory), cancellationToken);
                writing = new ArchiveProgress(bytes, files, done => progress?.Report(done * WritingShare), status,
                    options.Password is null ? WorkPhases.Writing : WorkPhases.Encrypting);
            }
            await ArchiveWriter.WriteAsync(ContentsOf(folder.UnzippedDirectory, "", zipped, Opened), options, folder.NewArchivePath,
                cancellationToken, writing);
            DurableFiles.FlushFile(folder.NewArchivePath);
            Reached(ArchiveStep.ArchiveWritten);

            int checkedFiles = 0;
            int toCheck = Math.Max(1, zipped.FileCount);
            status?.Invoke(new WorkStatus(WorkPhases.Checking, 0, zipped.FileCount));
            void Checked()
            {
                // (Counted apart from the report, which may be left out)
                checkedFiles++;
                progress?.Report(WritingShare + (1 - WritingShare) * Math.Min(0.999, (double)checkedFiles / toCheck));
                status?.Invoke(new WorkStatus(WorkPhases.Checking, Math.Min(checkedFiles, zipped.FileCount), zipped.FileCount));
            }
            FolderManifest inArchive = await Task.Run(() => ArchiveReading.ManifestOf(folder.NewArchivePath, options.Format, options.Password,
                cancellationToken, Checked), cancellationToken);
            if (!inArchive.SameAs(zipped))
                throw new ArchiveUnreadableException($"The new archive of {folder.UnzippedDirectory} doesn't hold what was zipped.");
            Reached(ArchiveStep.ArchiveChecked);

            // The archive it replaces wasn't encrypted: overwritten first (the copy, which counts, is still there)
            status?.Invoke(new WorkStatus(WorkPhases.Replacing));
            if (folder.ShredsReplacedArchives)
                Shredding.Overwrite(folder.ArchivePath, OverwritePasses);
            File.Move(folder.NewArchivePath, folder.ArchivePath, overwrite: true);
            DurableFiles.FlushDirectory(Path.GetDirectoryName(folder.ArchivePath)!);
            Reached(ArchiveStep.ArchiveReplaced);
        }
        catch
        {
            // The old archive and the copy are as they were: only the new archive is to be taken away
            DeleteIfThere(folder.NewArchivePath);
            throw;
        }

        // The archive of the format it had: gone before the copy, so that two archives are only ever there while the copy counts. If it
        // can't be deleted, zipping fails here (the copy stays, and counts).
        if (folder.FormerArchivePath is { } former && !string.Equals(former, folder.ArchivePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(former))
        {
            if (folder.ShredsReplacedArchives)
                Shredding.Overwrite(former, OverwritePasses);
            File.Delete(former);
            DurableFiles.FlushDirectory(Path.GetDirectoryName(folder.ArchivePath)!);
        }

        // From here on, the archive holds everything: the copy can go. Renamed first, so that it is gone at once and a half deleted copy
        // is never taken for the folder.
        string trash = Path.Combine(TrashDirectory(folder), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        Directory.Move(folder.UnzippedDirectory, trash);
        DurableFiles.FlushDirectory(Path.GetDirectoryName(folder.UnzippedDirectory)!);
        Reached(ArchiveStep.UnzippedMovedAway);

        // (The copy of an encrypted one is what the encryption is for: overwritten before it is deleted)
        bool shred = options.Password is not null;
        string deleting = shred ? WorkPhases.Overwriting : WorkPhases.Deleting;
        int toDelete = status is null ? 0 : CountFiles(trash);
        status?.Invoke(new WorkStatus(deleting, 0, toDelete));
        DeleteTrash(trash, afterDeletingSome: () => Reached(ArchiveStep.DeletingUnzipped), shred,
            deleted => status?.Invoke(new WorkStatus(deleting, Math.Min(deleted, toDelete), toDelete)));
    }

    /// <summary>How many files are in the directory (for telling how far deleting it got), 0 if it can't be told.</summary>
    private static int CountFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).Count();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>How much of zipping's progress is writing the archive; reading it back is the rest.</summary>
    private const double WritingShare = 0.7;

    /// <summary>
    /// What the files of the unzipped copy have together, as <see cref="ContentsOf"/> takes them (no links, not <c>_/temp</c>): only for
    /// telling how far zipping got, so what can't be read is skipped (the writer fails on it).
    /// </summary>
    private static (long Bytes, int Files) MeasureCopy(string root)
    {
        long bytes = 0;
        int files = 0;
        EnumerationOptions options = new() { AttributesToSkip = 0, IgnoreInaccessible = true };
        string leftOut = Path.Combine(root, PathService.ReservedName, "temp");
        Stack<string> pending = new([root]);
        while (pending.TryPop(out string? directory))
        {
            try
            {
                foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
                {
                    if (PathService.IsLink(entry))
                        continue;
                    if (entry is FileInfo file)
                    {
                        bytes += file.Length;
                        files++;
                    }
                    else if (!string.Equals(entry.FullName, leftOut, StringComparison.OrdinalIgnoreCase))
                        pending.Push(entry.FullName);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Only for telling how far it got
            }
        }
        return (bytes, files);
    }

    /// <summary>
    /// The unzipped copy as the writer lists it, everything in it but links (which could point anywhere), with the hash of each file noted in
    /// <paramref name="manifest"/> as it is read.
    /// </summary>
    private ZipFolderEntry ContentsOf(string directory, string relativePath, FolderManifest manifest, Action opened)
        => new(Path.GetFileName(directory), () => new DirectoryInfo(directory).EnumerateFileSystemInfos("*", ListOptions)
            .Where(entry =>
            {
                // The temporary files of the folder's own data (_/temp: half done work, and the copies of archived folders unzipped inside
                // it, which are zipped into their own archives first) are never part of it
                if (entry is DirectoryInfo && relativePath == PathService.ReservedName && entry.Name == "temp")
                    return false;
                if (!PathService.IsLink(entry))
                    return true;
                logger.LogWarning("{link} is a link, which is left out of the archive", entry.FullName);
                return false;
            })
            .Select(entry =>
            {
                string path = relativePath == "" ? entry.Name : relativePath + "/" + entry.Name;
                if (entry is DirectoryInfo inner)
                {
                    manifest.AddDirectory(path);
                    return (ZipEntry)ContentsOf(inner.FullName, path, manifest, opened);
                }
                return new ZipFileEntry(entry.Name, entry.LastWriteTime, () =>
                {
                    opened();
                    FileStream file = new(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                    return new HashingReadStream(file, hash => manifest.AddFile(path, hash));
                });
            }));

    // ---- Unzipping

    /// <summary>
    /// Unzips the archive into the unzipped copy, which must not be there yet. Throws an <see cref="IOException"/> if it fails
    /// (<see cref="ArchiveUnreadableException"/> if the archive can't be read, also for a wrong password), nothing being unzipped then.
    /// </summary>
    /// <param name="progress">Told how much is done (0 to 1) as it goes.</param>
    /// <param name="status">Told what it is doing as it goes (<see cref="WorkStatus"/>), for the text under the bar.</param>
    public async Task UnzipAsync(ArchivedFolder folder, string? password, IProgress<double>? progress, CancellationToken cancellationToken,
        Action<WorkStatus>? status = null)
    {
        if (Directory.Exists(folder.UnzippedDirectory))
            throw new IOException($"{folder.UnzippedDirectory} is unzipped already.");

        string staging = Path.Combine(StagingDirectory(folder), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(staging);
        try
        {
            await Task.Run(() => Extract(folder, password, staging, progress, status, cancellationToken), cancellationToken);
            status?.Invoke(new WorkStatus(WorkPhases.Placing));
            DurableFiles.FlushTree(staging);
            Reached(ArchiveStep.Unzipped);

            _ = Directory.CreateDirectory(Path.GetDirectoryName(folder.UnzippedDirectory)!);
            Directory.Move(staging, folder.UnzippedDirectory);
            DurableFiles.FlushDirectory(Path.GetDirectoryName(folder.UnzippedDirectory)!);
            Reached(ArchiveStep.UnzippedInPlace);
        }
        catch
        {
            DeleteTrash(staging, afterDeletingSome: null, shred: password is not null);
            throw;
        }
    }

    private void Extract(ArchivedFolder folder, string? password, string staging, IProgress<double>? progress, Action<WorkStatus>? status,
        CancellationToken cancellationToken)
    {
        int entries = ArchiveReading.CountEntries(folder.ArchivePath, folder.Format, password);
        int total = Math.Max(1, entries);
        int done = 0;
        status?.Invoke(new WorkStatus(WorkPhases.Unzipping, 0, entries));
        int files = 0;
        ArchiveReading.ForEachEntry(folder.ArchivePath, folder.Format, password, (entry, contents) =>
        {
            string target = TargetOf(staging, entry.Path, folder.ArchivePath);
            if (contents is null)
                _ = Directory.CreateDirectory(target);
            else
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // CreateNew: an entry that is there twice (or twice but for case, where the file system doesn't tell them apart) fails rather
                // than one replacing the other
                using (FileStream file = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
                    contents.CopyTo(file);
                if (entry.LastWriteTimeUtc is { } time)
                    File.SetLastWriteTimeUtc(target, time);
                if (++files == 1)
                    Reached(ArchiveStep.Unzipping);
            }
            done++;
            progress?.Report(Math.Min(1, (double)done / total));
            status?.Invoke(new WorkStatus(WorkPhases.Unzipping, Math.Min(done, entries), entries));
        }, cancellationToken);
    }

    /// <summary>
    /// Where the entry goes in <paramref name="staging"/>. An entry whose path would lead out of it (<c>..</c>, a drive, a path from the root),
    /// or that no file could be named, makes the archive one that can't be unzipped.
    /// </summary>
    internal static string TargetOf(string staging, string entryPath, string archivePath)
    {
        string[] parts = entryPath.Split('/');
        // (What a name can't have is up to the system: on Windows also "\" and ":", which are fine in a name on Linux)
        if (entryPath.Length == 0 || parts.Any(part => part is "" or "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArchiveUnreadableException($"{archivePath} has an entry that can't be unzipped: {entryPath}");

        string target = Path.GetFullPath(Path.Combine([staging, .. parts]));
        if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArchiveUnreadableException($"{archivePath} has an entry that can't be unzipped: {entryPath}");
        return target;
    }

    // ---- After a crash

    /// <summary>
    /// What the folder is in, after taking away what a zipping that didn't finish left (the new archive, which isn't the one that counts until
    /// it is renamed). What an unzipped copy of <see cref="ArchivedFolderState.Unzipped"/> holds is the folder's.
    /// </summary>
    public ArchivedFolderState Recover(ArchivedFolder folder)
    {
        string folderPath = Path.GetDirectoryName(folder.ArchivePath)!;
        foreach (string written in ArchivedFolder.ArchivePathsIn(folderPath).Select(path => path + ".new").Where(File.Exists))
        {
            logger.LogWarning("Deleting {archive}, which was being written when the app stopped", written);
            DeleteIfThere(written);
        }

        if (Directory.Exists(folder.UnzippedDirectory))
        {
            // Both formats: the app stopped while the filetype was changed, between the new archive taking its place and the old one being
            // deleted. The copy counts; the older archive is the one that was being replaced.
            string[] archives = [.. ArchivedFolder.ArchivePathsIn(folderPath).Where(File.Exists)];
            if (archives.Length == 2)
            {
                string older = archives.MinBy(File.GetLastWriteTimeUtc)!;
                logger.LogWarning("Deleting {archive}, which was being replaced by the other format when the app stopped", older);
                DeleteIfThere(older);
            }
            return ArchivedFolderState.Unzipped;
        }
        return File.Exists(folder.ArchivePath) ? ArchivedFolderState.Zipped : ArchivedFolderState.Missing;
    }

    /// <summary>
    /// Deletes what is left in the work directory: copies that were being unzipped (the archive still counts for them) and unzipped copies that
    /// were being deleted (their archive holds everything). Whatever is there is never needed. Returns how many were deleted.
    /// </summary>
    public int RecoverWorkDirectory(string workDirectory)
    {
        int deleted = 0;
        foreach (string kind in new[] { StagingName, TrashName })
        {
            string directory = Path.Combine(workDirectory, kind);
            if (!Directory.Exists(directory))
                continue;
            foreach (string left in Directory.EnumerateDirectories(directory))
            {
                logger.LogInformation("Deleting {directory}, which was left by zipping or unzipping when the app stopped", left);
                // (Whether it was of an encrypted folder isn't known any more: overwritten in any case)
                DeleteTrash(left, afterDeletingSome: null, shred: true);
                deleted++;
            }
        }
        return deleted;
    }

    // ---- The work directory

    private const string StagingName = "unzipping";
    private const string TrashName = "deleting";

    private static string StagingDirectory(ArchivedFolder folder) => Path.Combine(folder.WorkDirectory, StagingName);
    private static string TrashDirectory(ArchivedFolder folder) => Path.Combine(folder.WorkDirectory, TrashName);

    private void Reached(ArchiveStep step) => StepReached?.Invoke(step);

    private void DeleteIfThere(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete {file}", file);
        }
    }

    /// <summary>
    /// Deletes a directory of the work directory, files first. Failing doesn't fail what deletes it: the next start deletes what is left.
    /// </summary>
    /// <param name="afterDeletingSome">Called once the first file is deleted (for the tests: a copy that is partly deleted).</param>
    /// <param name="shred">Whether each file is overwritten first (<see cref="Shredding"/>).</param>
    /// <param name="deleted">Told how many files were deleted so far, after each.</param>
    private void DeleteTrash(string directory, Action? afterDeletingSome, bool shred, Action<int>? deleted = null)
    {
        int count = 0;
        try
        {
            if (!Directory.Exists(directory))
                return;
            bool first = true;
            foreach (string file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                if (shred)
                    Shredding.Overwrite(file, OverwritePasses);
                File.Delete(file);
                deleted?.Invoke(++count);
                if (first)
                    afterDeletingSome?.Invoke();
                first = false;
            }
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete {directory}; it is deleted at the next start", directory);
        }
    }
}
