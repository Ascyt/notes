using System.Collections.Concurrent;
using System.Diagnostics;
using Notes.Features.Bridges;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.ZippedFolders;

public sealed partial class ArchivedFolders
{
    // ---- Turning it on

    /// <summary>
    /// Makes the folder (whose configuration now says <c>"archive": { "enabled": true }</c>) an archived one: everything in it becomes its
    /// unzipped copy, which is mounted where it was, and zipped once it isn't used for a while. In one step that either happens or doesn't
    /// (the folder is renamed into the copy), then its configuration is put back where the folder was. The caller holds the
    /// <see cref="StructureLock"/>.
    /// </summary>
    /// <exception cref="IOException">If the folder can't be moved (a file of it is open, on Windows). Nothing changed then.</exception>
    public async Task ConvertAsync(string folderPath)
    {
        string folder = Normalize(folderPath);
        FolderState state = StateOf(folder);
        await state.Gate.WaitAsync();
        try
        {
            if (MountedAt(folder) is not null || ArchiveFileOf(folder) is not null)
                return;

            // Archived folders unzipped inside it are zipped first: they are mounted where it is now
            foreach (EntryLocation inner in locations.UnzippedArchives.Where(archive => IsSameOrInside(archive.MountPath!, folder)).ToList())
                _ = await ZipAsync(inner.MountPath!);

            ArchivedFolder archived = FolderOf(folder);
            if (Directory.Exists(archived.UnzippedDirectory))
                throw new IOException($"{archived.UnzippedDirectory} is there already.");

            _ = Directory.CreateDirectory(Path.GetDirectoryName(archived.UnzippedDirectory)!);
            Directory.Move(folder, archived.UnzippedDirectory);
            DurableFiles.FlushDirectory(Path.GetDirectoryName(archived.UnzippedDirectory)!);
            DurableFiles.FlushDirectory(Path.GetDirectoryName(folder)!);
            PutBackConfiguration(folder, archived.UnzippedDirectory);

            _ = Mount(folder, archived.UnzippedDirectory, state);
        }
        finally
        {
            _ = state.Gate.Release();
        }
    }

    /// <summary>
    /// After the folder was renamed into its unzipped copy (by <see cref="ConvertAsync"/>, or before a crash): the folder is made again, with its
    /// configuration, which stays next to the archive.
    /// </summary>
    private void PutBackConfiguration(string folder, string unzipped)
    {
        _ = Directory.CreateDirectory(folder);
        string inCopy = Path.Combine(unzipped, PathService.ConfigFileName);
        string outside = Path.Combine(folder, PathService.ConfigFileName);
        if (File.Exists(inCopy) && !File.Exists(outside))
            File.Move(inCopy, outside);
        DurableFiles.FlushDirectory(folder);
        // (What was in it is in its copy now, which mounting it walks)
        configs.TreeChanged(folder);
    }

    // ---- Turning it off

    /// <summary>In the work directory (<c>_/temp/zipping</c>): the copies of folders being turned off, each with a marker naming the folder.</summary>
    private const string TurningOffKind = "turning-off";

    /// <summary>In the data of a location (<c>_</c>): the histories of folders that were archived, set aside when it was turned off.</summary>
    public const string ArchivedHistoryName = "archived-history";

    /// <summary>
    /// Makes an archived folder whose archiving is turned off (its configuration, or the one above that archived it, says so now) an ordinary
    /// folder again: its contents go back into it, its archive is deleted, and its own history is set aside (<see cref="ArchivedHistoryName"/>),
    /// as it can't be part of the history of the folders around it. Archived folders inside it stay archived (they go along as they are).
    /// Whether it is an ordinary folder now: false if it is still archived as the configurations say, if that can't be told (an invalid one),
    /// or if something uses it (30 s at most; nothing changed then). The caller holds the <see cref="StructureLock"/>.
    /// </summary>
    /// <remarks>
    /// Never loses anything: a marker naming the folder is written first, then the unzipped copy is renamed into the work directory next to it
    /// (or the archive unzipped there), and from then on that copy is what counts. What follows (deleting the archive, setting the history
    /// aside, moving the entries into the folder one by one) is finished at the next start if the app stops (<see cref="RecoverAsync"/>).
    /// </remarks>
    /// <param name="waitForUses">Whether what uses it is waited for (30 s at most), or it is left for the next time at once (the job).</param>
    public async Task<bool> TurnOffAsync(string folderPath, bool waitForUses = true)
    {
        string folder = Normalize(folderPath);
        // (Never the root or a bridge, whatever else says so: they are never archived)
        if (resolver.IsLocationRoot(folder) || IsLocation(folder))
            return false;
        FolderState state = StateOf(folder);
        await state.Gate.WaitAsync();
        try
        {
            if (!resolver.IsArchivedOnDisk(folder))
                return true;
            if (resolver.Resolve(folder) is not { IsCertain: true, Settings: null })
                return false;

            // Those unzipped inside it are zipped first: their copies are in its copy's _/temp, which doesn't go along
            if (MountedAt(folder) is { } copy)
                foreach (EntryLocation inner in locations.UnzippedArchives.Where(other => other != copy
                             && other.PhysicalPath.StartsWith(copy.PhysicalPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(other => other.PhysicalPath.Length).ToList())
                    if (!await ZipAsync(inner.MountPath!))
                        return false;

            // Nothing may use it while its entries move (an open file can't be moved, on Windows)
            DateTime giveUp = DateTime.UtcNow + MaxWaitForUses;
            while (Volatile.Read(ref state.Users) > 0)
            {
                if (!waitForUses || DateTime.UtcNow > giveUp)
                    return false;
                await Task.Delay(20);
            }

            ArchivedFolder archived = FolderOf(folder);
            EntryLocation location = locations.LocationOf(folder) ?? locations.Root;
            string work = Path.Combine(archived.WorkDirectory, TurningOffKind);
            _ = Directory.CreateDirectory(work);
            string staging = Path.Combine(work, Guid.NewGuid().ToString("N"));
            string marker = staging + MarkerExtension;
            // (Relative to its location, which may be mounted elsewhere at the next start)
            File.WriteAllText(marker, Path.GetRelativePath(location.PhysicalPath, folder));
            DurableFiles.FlushFile(marker);
            DurableFiles.FlushDirectory(work);

            EntryLocation? mounted = MountedAt(folder);
            if (mounted is not null)
            {
                history.ArchiveZipping(mounted);
                locations.UnmountArchive(mounted);
                configs.LocationUnmounted(mounted);
            }
            try
            {
                // The copy counts if it is there (also one a crash left, not mounted); otherwise the archive is unzipped into place
                if (Directory.Exists(archived.UnzippedDirectory))
                {
                    Directory.Move(archived.UnzippedDirectory, staging);
                    DurableFiles.FlushDirectory(Path.GetDirectoryName(archived.UnzippedDirectory)!);
                    DurableFiles.FlushDirectory(work);
                }
                else
                    await archiver.UnzipAsync(archived with { UnzippedDirectory = staging }, PasswordToUnzip(folder, archived), progress: null,
                        lifetime.ApplicationStopping);
            }
            catch (Exception e)
            {
                // Nothing was moved: it is as it was
                logger.LogError(e, "Could not turn off archiving of {folder}; it stays archived", folder);
                if (mounted is not null && Directory.Exists(archived.UnzippedDirectory))
                    _ = Mount(folder, archived.UnzippedDirectory, state);
                File.Delete(marker);
                return false;
            }

            FinishTurningOff(location, folder, staging, marker);
            logger.LogInformation("Turned off archiving of /{folder}", locations.ToVirtualPath(folder));
            return true;
        }
        finally
        {
            _ = state.Gate.Release();
        }
    }

    private const string MarkerExtension = ".target";

    /// <summary>
    /// The password an encrypted archive of the folder is unzipped with when it is turned off: of the folder that set it up (its own, or the
    /// <c>onLowestLevel</c> one above that archived it, as the configurations said then; the folder's own is tried if that one says nothing now).
    /// </summary>
    /// <exception cref="IOException">If the archive is encrypted and the password isn't known: nothing is changed then.</exception>
    private string? PasswordToUnzip(string folder, ArchivedFolder archived)
    {
        if (!ArchiveReading.IsEncrypted(archived.ArchivePath, archived.Format))
            return null;
        return EncryptionPasswordFolders(folder).Select(passwords.PasswordOf).FirstOrDefault(known => known is not null)
               ?? throw new IOException($"{archived.ArchivePath} is encrypted, and the password to open it isn't known: unlock it first.");
    }

    /// <summary>
    /// The folders whose password may have encrypted the folder's archive: the folder itself, and the folders above (an <c>onLowestLevel</c>
    /// one), nearest first. (Turned off, the configurations no longer say which.)
    /// </summary>
    private IEnumerable<string> EncryptionPasswordFolders(string folder)
    {
        for (string? up = folder; up is not null; up = IsLocation(up) ? null : locations.ParentOf(up))
            yield return OuterOf(up);
    }

    /// <summary>
    /// Whether the folder (physical), or a folder below it whose archiving it decides, is zipped encrypted with a password the app doesn't know
    /// now: turning archiving (or encryption) off there has to wait until it is unlocked.
    /// </summary>
    public bool NeedsUnknownPassword(string folderPath)
    {
        IEnumerable<string> folders = [Normalize(folderPath), .. DirectoriesBelow(folderPath).Zipped];
        return folders.Where(folder => ArchiveFileOf(folder) is not null && MountedAt(folder) is null).Any(folder =>
        {
            ArchivedFolder archived = FolderOf(folder);
            return ArchiveReading.IsEncrypted(archived.ArchivePath, archived.Format)
                   && !EncryptionPasswordFolders(folder).Any(passwords.Knows);
        });
    }

    /// <summary>
    /// Once the folder's copy is in <paramref name="staging"/> (and counts): its archive is deleted, its own history set aside, and its entries
    /// moved into the folder; then the staging directory and the marker go. Each step may have happened already (after a crash).
    /// </summary>
    /// <exception cref="IOException">If an entry can't be moved (one of its name is in the folder): the rest stays in staging, and counts.</exception>
    private void FinishTurningOff(EntryLocation location, string folder, string staging, string marker)
    {
        _ = Directory.CreateDirectory(folder);
        foreach (string archive in ArchivedFolder.ArchivePathsIn(folder).SelectMany(path => new[] { path, path + ".new" }).Where(File.Exists))
            File.Delete(archive);
        DurableFiles.FlushDirectory(folder);

        // Its own history can't be part of the history around it (a repository inside another one's work tree): set aside, never deleted
        string data = Path.Combine(staging, PathService.ReservedName);
        string ownHistory = Path.Combine(data, "history");
        if (Directory.Exists(ownHistory))
        {
            string aside = Path.Combine(location.DataDirectory, ArchivedHistoryName, Path.GetRelativePath(location.PhysicalPath, folder),
                time.GetUtcNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            if (Directory.Exists(aside))
                aside += "-" + Guid.NewGuid().ToString("N")[..8];
            _ = Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
            Directory.Move(ownHistory, aside);
            DurableFiles.FlushDirectory(Path.GetDirectoryName(aside)!);
            logger.LogInformation("The history of /{folder} from while it was archived is kept at {aside}", locations.ToVirtualPath(folder), aside);
        }
        // What else is in its _ is the work of zipping and unzipping inside it (temp): never needed
        if (Directory.Exists(data))
            DeleteTree(data);

        foreach (string entry in Directory.EnumerateFileSystemEntries(staging).ToList())
        {
            string target = Path.Combine(folder, Path.GetFileName(entry));
            if (Path.Exists(target))
                throw new IOException($"{target} is there already; what is left of the folder is in {staging}.");
            if (Directory.Exists(entry))
                Directory.Move(entry, target);
            else
                File.Move(entry, target);
        }
        DurableFiles.FlushDirectory(folder);

        // (What was in its copy is in it again)
        configs.TreeChanged(folder);
        Directory.Delete(staging);
        File.Delete(marker);
        DurableFiles.FlushDirectory(Path.GetDirectoryName(marker)!);
    }

    /// <summary>Finishes turning off what the app was turning off when it stopped (the markers of the location's work directory).</summary>
    private void RecoverTurningOff(EntryLocation location)
    {
        string work = Path.Combine(location.DataDirectory, "temp", ZippingKind, TurningOffKind);
        if (!Directory.Exists(work))
            return;
        foreach (string marker in Directory.EnumerateFiles(work, "*" + MarkerExtension).ToList())
        {
            string staging = marker[..^MarkerExtension.Length];
            if (!Directory.Exists(staging))
            {
                // Stopped before the copy was moved there: nothing changed, and turning it off is tried again (ReconcileAsync)
                File.Delete(marker);
                continue;
            }
            string folder = Path.GetFullPath(Path.Combine(location.PhysicalPath, File.ReadAllText(marker).Trim()));
            logger.LogWarning("Archiving of {folder} was being turned off when the app stopped; it is finished now", folder);
            try
            {
                FinishTurningOff(location, folder, staging, marker);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogError(e, "Could not finish turning off archiving of {folder}; what is left of it is in {staging}", folder, staging);
            }
        }
    }

    /// <summary>Deletes a directory with everything in it, read-only files too (git writes its objects that way).</summary>
    private static void DeleteTree(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    // ---- Bringing what is on disk in line with the configurations

    /// <summary>
    /// The folders at or below the directory (physical; also inside those that are unzipped) whose archiving is to be turned off (archived on
    /// disk, while the configurations certainly say they aren't), and those to be turned on (archived as they say, not on disk yet: also the
    /// lowest level below an <c>onLowestLevel</c> folder). The outermost first.
    /// </summary>
    private (List<string> TurnOff, List<string> TurnOn) Differences(string root)
    {
        List<string> turnOff = [];
        List<string> turnOn = [];
        Queue<string> pending = new([Normalize(root)]);
        EnumerationOptions options = new() { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        while (pending.TryDequeue(out string? directory))
        {
            if (!Directory.Exists(directory))
                continue;
            // (Remembered for the job, which converts what comes to hold files below it)
            if (configs.Get(directory).Config?.OwnArchive is { Enabled: true, OnLowestLevel: true })
                _ = _lowestLevelFolders.TryAdd(Normalize(directory), 0);
            bool onDisk = resolver.IsArchivedOnDisk(directory);
            ResolvedArchive resolved = resolver.Resolve(directory);
            if (onDisk && resolved is { IsCertain: true, Settings: null })
                turnOff.Add(directory);
            else if (!onDisk && resolved.Settings is not null && !IsLocation(directory) && !locations.HoldsBridge(directory))
                turnOn.Add(directory);

            // What is in it: in its copy, if it is unzipped; nothing to look at if it is zipped (its contents are in its archive)
            string? contents = MountedAt(directory) is { } copy ? copy.PhysicalPath : onDisk ? null : directory;
            if (contents is null)
                continue;
            foreach (string inner in Directory.EnumerateDirectories(contents, "*", options).Order(StringComparer.OrdinalIgnoreCase))
            {
                // (An unzipped archived folder's own place counts as hidden by it, like a bridge's: it is looked at, a bridge is not)
                if ((Path.GetFileName(inner) == PathService.ReservedName && IsLocation(contents))
                    || (locations.IsHiddenByBridge(inner) && MountedAt(inner) is null) || IsLocation(inner))
                    continue;
                pending.Enqueue(inner);
            }
        }
        return (turnOff, turnOn);
    }

    /// <summary>
    /// Turns archiving off and on where the configurations say so now, at or below the directory (physical): after a configuration was saved,
    /// at the start, and for <c>onLowestLevel</c> folders by the job (folders that came to hold files). The caller holds the
    /// <see cref="StructureLock"/>. What can't be done now (in use) is logged, and done the next time. The folders that couldn't be turned off.
    /// </summary>
    /// <param name="waitForUses">As for <see cref="TurnOffAsync"/>.</param>
    public async Task<List<string>> ReconcileAsync(string root, bool waitForUses = true)
    {
        // Those below it are found again (what the configurations say now)
        foreach (string known in _lowestLevelFolders.Keys.Where(known => IsSameOrInside(known, root)))
            _ = _lowestLevelFolders.TryRemove(known, out _);

        List<string> failed = [];
        // Turning one off brings what is in it to light (archived folders that were in its archive): looked at again, a few rounds at most
        for (int round = 0; round < 8; round++)
        {
            (List<string> turnOff, List<string> turnOn) = Differences(root);
            turnOff.RemoveAll(failed.Contains);
            // (Nothing inside one that couldn't be turned off: its contents are still in its copy, where they don't stay)
            turnOn.RemoveAll(folder => failed.Any(stuck => IsSameOrInside(folder, stuck)
                                                            || IsSameOrInside(folder, resolver.UnzippedDirectoryOf(stuck))));
            if (turnOff.Count == 0 && turnOn.Count == 0)
                break;

            if (turnOff.Count > 0)
            {
                foreach (string folder in turnOff)
                    if (!await TurnOffAsync(folder, waitForUses))
                    {
                        logger.LogWarning("Archiving of {folder} is turned off, but it couldn't be undone now (in use); tried again later", folder);
                        failed.Add(folder);
                    }
                // What is to be turned on is looked for again: the contents of those turned off are where they belong now
                continue;
            }

            foreach (string folder in turnOn)
            {
                try
                {
                    await ConvertAsync(folder);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(e, "Could not make {folder} an archived folder now; tried again later", folder);
                    failed.Add(folder);
                }
            }
        }
        return failed;
    }

    /// <summary>
    /// <see cref="ReconcileAsync"/> for the root and every bridge (at the start, and by the soft restart): also finds the <c>onLowestLevel</c>
    /// folders again, those in unzipped folders included. The caller holds the <see cref="StructureLock"/>.
    /// </summary>
    public async Task ReconcileEverywhereAsync()
    {
        foreach (EntryLocation location in locations.All.Where(location => !location.IsArchive).ToList())
        {
            // (Walks every directory of the location, which takes a while with many)
            logger.LogInformation("Checking which folders of {location} are to be archived or not", location.PhysicalPath);
            Stopwatch stopwatch = Stopwatch.StartNew();
            _ = await ReconcileAsync(location.PhysicalPath);
            logger.LogInformation("Checked {location} after {seconds:0.0} s", location.PhysicalPath, stopwatch.Elapsed.TotalSeconds);
        }
    }

    /// <summary>
    /// After something was put into the directory (physical: a file created or uploaded, a folder copied or moved in): below an
    /// <c>onLowestLevel</c> folder, it or what is in it may hold files now, and is archived at once, so that it is one like the others right
    /// away (unzipped, with its mark). Elsewhere nothing happens. The caller holds the <see cref="StructureLock"/>.
    /// </summary>
    public async Task ArchiveLowestLevelAfterChangeAsync(string physicalDirectory)
    {
        string directory = WhereItIsNow(physicalDirectory);
        if (Directory.Exists(directory) && resolver.IsBelowLowestLevel(directory))
            _ = await ReconcileAsync(OuterOf(directory));
    }

    private bool IsLocation(string physicalPath)
        => locations.All.Any(location => IsSame(location.PhysicalPath, physicalPath));

    /// <summary>
    /// The directories below the folder (physical), as the entries have them: those of an unzipped folder in its copy, none of a zipped one
    /// (they are in its archive), none of a bridge or of the app's own data. Each with the zipped folders it couldn't look into.
    /// </summary>
    public (List<string> Directories, List<string> Zipped) DirectoriesBelow(string folderPath)
    {
        List<string> found = [];
        List<string> zipped = [];
        Stack<string> pending = new([MountedAt(folderPath)?.PhysicalPath ?? Normalize(folderPath)]);
        EnumerationOptions options = new() { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        while (pending.TryPop(out string? directory))
        {
            foreach (string inner in Directory.EnumerateDirectories(directory, "*", options))
            {
                if ((Path.GetFileName(inner) == PathService.ReservedName && IsLocation(directory))
                    || (locations.IsHiddenByBridge(inner) && MountedAt(inner) is null) || IsLocation(inner))
                    continue;
                found.Add(inner);
                if (MountedAt(inner) is { } copy)
                    pending.Push(copy.PhysicalPath);
                else if (ArchiveFileOf(inner) is not null)
                    zipped.Add(inner);
                else
                    pending.Push(inner);
            }
        }
        return (found, zipped);
    }

    /// <summary>The <c>onLowestLevel</c> folders found (by physical path), for <see cref="ReconcileLowestLevelsAsync"/>.</summary>
    private readonly ConcurrentDictionary<string, byte> _lowestLevelFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Converts the folders below each <c>onLowestLevel</c> folder that came to hold files (a background job). Those found at the start or when
    /// a configuration was saved; one inside an archived folder only while that is unzipped. The caller holds the <see cref="StructureLock"/>.
    /// </summary>
    public async Task ReconcileLowestLevelsAsync()
    {
        foreach (string folder in _lowestLevelFolders.Keys.ToList())
            if (Directory.Exists(folder))
                _ = await ReconcileAsync(folder, waitForUses: false);
            else
                _ = _lowestLevelFolders.TryRemove(folder, out _);

        // Unzipped ones whose archiving was turned off, but which were in use then
        foreach (string folder in UnzippedFolders())
            if (resolver.Resolve(folder) is { IsCertain: true, Settings: null })
                _ = await TurnOffAsync(folder, waitForUses: false);
    }
}
