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
public sealed class FolderArchiver(ILogger<FolderArchiver> logger)
{
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
    public async Task ZipAsync(ArchivedFolder folder, ArchiveOptions options, CancellationToken cancellationToken)
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
            await ArchiveWriter.WriteAsync(ContentsOf(folder.UnzippedDirectory, "", zipped, Opened), options, folder.NewArchivePath,
                cancellationToken);
            DurableFiles.FlushFile(folder.NewArchivePath);
            Reached(ArchiveStep.ArchiveWritten);

            FolderManifest inArchive = await Task.Run(() => ArchiveReading.ManifestOf(folder.NewArchivePath, options.Format, options.Password,
                cancellationToken), cancellationToken);
            if (!inArchive.SameAs(zipped))
                throw new ArchiveUnreadableException($"The new archive of {folder.UnzippedDirectory} doesn't hold what was zipped.");
            Reached(ArchiveStep.ArchiveChecked);

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

        // From here on, the archive holds everything: the copy can go. Renamed first, so that it is gone at once and a half deleted copy
        // is never taken for the folder.
        string trash = Path.Combine(TrashDirectory(folder), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        Directory.Move(folder.UnzippedDirectory, trash);
        DurableFiles.FlushDirectory(Path.GetDirectoryName(folder.UnzippedDirectory)!);
        Reached(ArchiveStep.UnzippedMovedAway);

        DeleteTrash(trash, afterDeletingSome: () => Reached(ArchiveStep.DeletingUnzipped));
    }

    /// <summary>
    /// The unzipped copy as the writer lists it, everything in it but links (which could point anywhere), with the hash of each file noted in
    /// <paramref name="manifest"/> as it is read.
    /// </summary>
    private ZipFolderEntry ContentsOf(string directory, string relativePath, FolderManifest manifest, Action opened)
        => new(Path.GetFileName(directory), () => new DirectoryInfo(directory).EnumerateFileSystemInfos("*", ListOptions)
            .Where(entry =>
            {
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
    public async Task UnzipAsync(ArchivedFolder folder, string? password, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (Directory.Exists(folder.UnzippedDirectory))
            throw new IOException($"{folder.UnzippedDirectory} is unzipped already.");

        string staging = Path.Combine(StagingDirectory(folder), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(staging);
        try
        {
            await Task.Run(() => Extract(folder, password, staging, progress, cancellationToken), cancellationToken);
            DurableFiles.FlushTree(staging);
            Reached(ArchiveStep.Unzipped);

            _ = Directory.CreateDirectory(Path.GetDirectoryName(folder.UnzippedDirectory)!);
            Directory.Move(staging, folder.UnzippedDirectory);
            DurableFiles.FlushDirectory(Path.GetDirectoryName(folder.UnzippedDirectory)!);
            Reached(ArchiveStep.UnzippedInPlace);
        }
        catch
        {
            DeleteTrash(staging, afterDeletingSome: null);
            throw;
        }
    }

    private void Extract(ArchivedFolder folder, string? password, string staging, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        int total = Math.Max(1, ArchiveReading.CountEntries(folder.ArchivePath, folder.Format, password));
        int done = 0;
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
            progress?.Report(Math.Min(1, (double)++done / total));
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
        if (File.Exists(folder.NewArchivePath))
        {
            logger.LogWarning("Deleting {archive}, which was being written when the app stopped", folder.NewArchivePath);
            DeleteIfThere(folder.NewArchivePath);
        }

        if (Directory.Exists(folder.UnzippedDirectory))
            return ArchivedFolderState.Unzipped;
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
                DeleteTrash(left, afterDeletingSome: null);
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
    private void DeleteTrash(string directory, Action? afterDeletingSome)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;
            bool first = true;
            foreach (string file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
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
