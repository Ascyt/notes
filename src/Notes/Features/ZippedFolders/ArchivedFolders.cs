using System.Collections.Concurrent;
using Notes.Features.Bridges;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// The archived folders (<c>"archive": { "enabled": true }</c>, see docs/development/zipped-folders.md): unzips one when a request reaches
/// into it, mounts the unzipped copy where the folder is (<see cref="EntryLocations.MountArchive"/>) with its history, and zips it again once
/// it wasn't used for a while (<see cref="ArchivesConfig.ZipAfterMinutes"/>). What is on disk is changed by <see cref="FolderArchiver"/>,
/// which never loses anything; this decides when, and makes sure nothing uses a copy while it is zipped:
/// <list type="bullet">
/// <item>Every request that goes into an unzipped folder holds a use of it (<see cref="EnterAsync"/>) until it is answered.</item>
/// <item>Zipping waits until nothing uses the folder, and holds its gate meanwhile, so nothing new starts to use it.</item>
/// <item>Unzipping holds the gate too: the folder is unzipped once, however many requests ask for it at once.</item>
/// </list>
/// </summary>
/// <remarks>
/// An archived folder inside another is unzipped into the unzipped copy of that one (its own <c>_/temp/unzipped</c>), once that one is,
/// and zipped before it. Archived folders are keyed by the physical path of the folder (where the archive and the configuration are).
/// Turning archiving on and off, and bringing the disk in line with the configurations, is in <c>ArchivedFolders.TurningOnAndOff.cs</c>;
/// what is done at the start, after a crash, in <c>ArchivedFolders.Recovery.cs</c>.
/// </remarks>
public sealed partial class ArchivedFolders(EntryLocations locations, IPathService paths, FolderConfigStore configs, FolderArchiver archiver,
    ArchiveSettingsResolver resolver, ArchivePasswords passwords, HistoryService history, ArchivesConfig settings, TimeProvider time,
    IHostApplicationLifetime lifetime, ILogger<ArchivedFolders> logger)
{
    public const string UnzippedKind = "unzipped";
    public const string ZippingKind = "zipping";

    private sealed class FolderState
    {
        /// <summary>Held while it is zipped or unzipped.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>The requests (and others) using the unzipped copy now.</summary>
        public int Users;

        /// <summary>When it was last used (by the app's clock, in ticks).</summary>
        public long LastUsedTicks;

        /// <summary>The unzipping that runs or ran last; a new one is started once that has ended.</summary>
        public UnzipJob? Unzipping;

        /// <summary>The zipping that runs now (null otherwise).</summary>
        public ZipJob? Zipping;
    }

    private readonly ConcurrentDictionary<string, FolderState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long zipping waits for what uses a folder, before it gives up (and the folder stays unzipped).</summary>
    private static readonly TimeSpan MaxWaitForUses = TimeSpan.FromSeconds(30);

    private FolderState StateOf(string folderPath)
        => _states.GetOrAdd(Normalize(folderPath), _ => new FolderState());

    // ---- What a folder is

    /// <summary>
    /// Where the path (physical) is now: in the unzipped copy if it is at or inside an archived folder that is mounted (it was found before the
    /// folder was turned into one, say, by a request that waited for the <see cref="StructureLock"/>); otherwise the path itself. Without
    /// this, what is written there would end up next to the archive, hidden by the copy.
    /// </summary>
    public string WhereItIsNow(string physicalPath)
    {
        string path = Normalize(physicalPath);
        // (Again for a folder unzipped inside one that was)
        for (bool moved = true; moved;)
        {
            moved = false;
            foreach (EntryLocation archive in locations.UnzippedArchives)
                if (IsSameOrInside(path, archive.MountPath!))
                {
                    path = Normalize(Path.Combine(archive.PhysicalPath, Path.GetRelativePath(archive.MountPath!, path)));
                    moved = true;
                    break;
                }
        }
        return path;
    }

    /// <summary>
    /// Whether the folder (its physical path, or that of its unzipped copy) is archived, as the configurations say now (its own, or an
    /// <c>onLowestLevel</c> one above: <see cref="ArchiveSettingsResolver"/>). One whose archiving was just turned on may not be converted yet.
    /// </summary>
    public bool IsArchived(string physicalDirectory)
        => resolver.SettingsOf(physicalDirectory) is not null;

    /// <summary>Whether the folder (its physical path, or that of its unzipped copy) is archived encrypted, as the configurations say now.</summary>
    public bool IsEncrypted(string physicalDirectory)
        => resolver.SettingsOf(physicalDirectory) is { Encrypted: true };

    /// <summary>The unzipped copy mounted for the folder (its physical path), or null if it is zipped (or not archived).</summary>
    public EntryLocation? MountedAt(string folderPath)
        => locations.UnzippedArchives.FirstOrDefault(archive => IsSame(archive.MountPath!, folderPath));

    /// <summary>
    /// The archived folders that are the entries (physical paths, as requests have them: an unzipped copy for one that is unzipped) or are
    /// somewhere inside them, also inside those that are unzipped, by the physical path of each folder (where its archive is).
    /// </summary>
    public List<string> ArchivedAtOrBelow(IEnumerable<string> physicalPaths)
    {
        List<string> found = [];
        void Visit(string folder)
        {
            found.Add(folder);
            if (MountedAt(folder) is { } mounted)
                foreach (string inner in ArchivedFoldersIn(mounted.PhysicalPath))
                    Visit(inner);
        }

        foreach (string path in physicalPaths.Where(Directory.Exists))
        {
            string folder = OuterOf(path);
            if (IsArchived(folder) && (MountedAt(folder) is not null || IsZipped(folder)))
                Visit(folder);
            else
                foreach (string inner in ArchivedFoldersIn(path))
                    Visit(inner);
        }
        return [.. found.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The archived folder whose unzipped copy the path is, or the path itself.</summary>
    public string OuterOf(string physicalPath)
        => locations.BridgeOf(physicalPath) is { IsArchive: true } archive ? archive.MountPath! : Normalize(physicalPath);

    /// <summary>
    /// Unzips every zipped archived folder that is one of the entries or inside them (also those found inside the ones it unzips), that
    /// <paramref name="mayUnzip"/> allows: before what is in them is checked as a whole (a copy, a move, a delete, a rename). The others
    /// stay zipped, and the check finds what it can't read in them.
    /// </summary>
    /// <exception cref="IOException">If one can't be unzipped (a damaged archive).</exception>
    public async Task UnzipAllAtOrBelowAsync(IEnumerable<string> physicalPaths, Func<string, bool> mayUnzip)
    {
        string[] entries = [.. physicalPaths];
        HashSet<string> refused = new(StringComparer.OrdinalIgnoreCase);
        for (;;)
        {
            List<string> zipped = [.. ArchivedAtOrBelow(entries).Where(folder => IsZipped(folder) && !refused.Contains(folder))];
            if (zipped.Count == 0)
                return;
            foreach (string folder in zipped)
            {
                if (!mayUnzip(folder))
                {
                    _ = refused.Add(folder);
                    continue;
                }
                if (await Unzip(folder).Finished is { } failure)
                    throw new IOException($"{folder} could not be unzipped: {failure}");
            }
        }
    }

    /// <summary>
    /// Zips every archived folder that is one of the entries or inside them, and keeps them zipped (holding their gates) while
    /// <paramref name="action"/> moves or deletes them: they go as their folders, archive and configuration. The entries are given to it as
    /// such folders (<see cref="OuterOf"/>), in the order given.
    /// </summary>
    /// <exception cref="ArchivePasswordNeededException">
    /// If an encrypted one can't be zipped because its password isn't known: nothing was done then.
    /// </exception>
    /// <exception cref="IOException">If one can't be zipped now (something in it is in use): nothing was done then.</exception>
    public async Task<T> WhileZippedAsync<T>(IReadOnlyList<string> physicalPaths, Func<IReadOnlyList<string>, Task<T>> action)
    {
        List<string> outer = [.. physicalPaths.Select(OuterOf)];
        for (;;)
        {
            // (Rather than waiting for its uses only to find that it can't be zipped)
            if (UnknownPasswordToZipAtOrBelow(physicalPaths) is { } needed)
                throw new ArchivePasswordNeededException(needed);

            foreach (string path in physicalPaths)
                if (!await ZipAtOrBelowAsync(path))
                    throw new IOException($"What is unzipped in {path} could not be zipped now.");

            // (Their gates in one order, so that two of these never wait for each other)
            List<FolderState> gates = [.. ArchivedAtOrBelow(outer).Order(StringComparer.OrdinalIgnoreCase).Select(StateOf)];
            foreach (FolderState state in gates)
                await state.Gate.WaitAsync();
            try
            {
                // Unzipped again before its gate was taken: zipped once more
                if (gates.Count > 0 && ArchivedAtOrBelow(outer).Any(folder => MountedAt(folder) is not null))
                    continue;
                return await action(outer);
            }
            finally
            {
                foreach (FolderState state in gates)
                    _ = state.Gate.Release();
            }
        }
    }

    /// <summary>
    /// The archive of the folder that is there on disk (<c>_.7z</c> or <c>_.zip</c>; the newer, if the app stopped while its format was
    /// changed), or null.
    /// </summary>
    public static string? ArchiveFileOf(string folderPath)
        => ArchivedFolder.ArchivePathsIn(folderPath).Where(File.Exists).MaxBy(File.GetLastWriteTimeUtc);

    /// <summary>
    /// Whether the folder is archived and zipped now: what is in it has to be unzipped to be seen. Also while it is being written (no longer
    /// mounted), when it may not have an archive yet (the first time): what is in it is then only in its copy, which unzipping it stops the
    /// zipping for, or waits for.
    /// </summary>
    public bool IsZipped(string folderPath)
        => MountedAt(folderPath) is null && (ArchiveFileOf(folderPath) is not null || ZipProgressOf(folderPath) is not null)
           && IsArchived(folderPath);

    /// <summary>
    /// How the directory (its physical path, or that of its unzipped copy) is kept now, for the mark after its name; null if it isn't an
    /// archived folder (or one just turned on, not converted yet).
    /// </summary>
    public ArchiveState? ArchiveStateOf(string physicalDirectory)
    {
        string folder = OuterOf(physicalDirectory);
        if (!IsArchived(folder))
            return null;
        // (Also while it waits for what uses it, still mounted, and while it is written, no longer mounted)
        if (ZipProgressOf(folder) is not null)
            return ArchiveState.Zipping;
        if (MountedAt(folder) is not null)
            return ArchiveState.Unzipped;
        return ArchiveFileOf(folder) is not null ? ArchiveState.Zipped : null;
    }

    /// <summary>How far the zipping of the folder (its physical path, or that of its unzipped copy) got, 0 to 1; null if it isn't zipped now.</summary>
    public double? ZipProgressOf(string physicalDirectory)
        => _states.TryGetValue(OuterOf(physicalDirectory), out FolderState? state) && Volatile.Read(ref state.Zipping) is { } job
            ? job.Progress
            : null;

    /// <summary>
    /// Where the folder's archive, its unzipped copy and the work directory of zipping are. The archive is the one it has (the newer, if the
    /// app stopped while the format was changed), or, without one, of the format it is to have.
    /// </summary>
    private ArchivedFolder FolderOf(string folderPath)
    {
        string folder = Normalize(folderPath);
        string archive = ArchivedFolder.ArchivePathsIn(folder).Where(File.Exists).MaxBy(File.GetLastWriteTimeUtc)
                         ?? Path.Combine(folder, ArchivedFolder.ArchiveFileName(resolver.SettingsOf(folder)?.Format ?? ArchiveFormat.SevenZip));
        return FolderOf(folder, archive);
    }

    /// <summary>As <see cref="FolderOf(string)"/>, with the archive at <paramref name="archivePath"/>.</summary>
    private ArchivedFolder FolderOf(string folder, string archivePath)
    {
        // In the data of the location the folder is in (for one inside another archived folder: in that one's unzipped copy)
        EntryLocation location = locations.LocationOf(folder) ?? locations.Root;
        return new ArchivedFolder(archivePath, resolver.UnzippedDirectoryOf(folder), Path.Combine(location.DataDirectory, "temp", ZippingKind));
    }

    // ---- Using it

    /// <summary>
    /// Holds a use of every unzipped folder along the virtual path (so none of them is zipped until the result is disposed), and finds the
    /// outermost archived folder along it that is zipped, which has to be unzipped first. With <paramref name="includingTarget"/> the entry
    /// at the path itself counts too (a directory that is listed, or that something is put into).
    /// </summary>
    /// <param name="usingTarget">
    /// Whether a use of the entry itself is held too, if it is an unzipped folder (not for a rename, which zips it before it renames it).
    /// </param>
    /// <returns>The physical path of the zipped folder, or null if nothing along the path is; and the uses, which the caller disposes.</returns>
    public async Task<(string? Zipped, IDisposable Uses)> EnterAsync(string virtualPath, bool includingTarget, bool usingTarget = true)
    {
        string[] names = virtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int count = includingTarget ? names.Length : names.Length - 1;
        for (;;)
        {
            Uses uses = new();
            bool changed = false;
            for (int length = 1; length <= count && !changed; length++)
            {
                if (paths.ToPhysicalPath(string.Join('/', names.Take(length))) is not { } physical || !Directory.Exists(physical))
                    break;

                if (locations.BridgeOf(physical) is { IsArchive: true } mounted)
                {
                    if (length == names.Length && !usingTarget)
                        continue;
                    uses.Add(await UseAsync(mounted.MountPath!));
                    // Zipped in the meantime: looked at again from the start
                    changed = MountedAt(mounted.MountPath!) is null;
                    continue;
                }

                if (IsZipped(physical))
                    return (physical, uses);
            }

            if (!changed)
                return (null, uses);
            uses.Dispose();
        }
    }

    /// <summary>A use of the unzipped folder: it isn't zipped until this is disposed (and then not before it wasn't used for a while).</summary>
    public async Task<IDisposable> UseAsync(string folderPath)
    {
        FolderState state = StateOf(folderPath);
        // Something needs what is in it now: a zipping that may be stopped is (the copy is still there), rather than waited for and undone
        Volatile.Read(ref state.Zipping)?.StopBecauseOpened();
        // (Waits while it is being zipped or unzipped)
        await state.Gate.WaitAsync();
        _ = Interlocked.Increment(ref state.Users);
        _ = state.Gate.Release();
        return new Use(() =>
        {
            Interlocked.Exchange(ref state.LastUsedTicks, time.GetUtcNow().UtcTicks);
            _ = Interlocked.Decrement(ref state.Users);
        });
    }

    /// <summary>
    /// A use of every unzipped folder that the entries (physical paths) are in, or that is inside them: for work that reads them after the
    /// request that asked for it was answered (an archive made to be downloaded).
    /// </summary>
    public async Task<IDisposable> UseAllAroundAsync(IEnumerable<string> physicalPaths)
    {
        string[] entries = [.. physicalPaths];
        Uses uses = new();
        foreach (EntryLocation archive in locations.UnzippedArchives.Where(archive => entries.Any(entry =>
                     locations.IsSameOrInside(entry, archive.PhysicalPath) || locations.IsSameOrInside(archive.PhysicalPath, entry))))
            uses.Add(await UseAsync(archive.MountPath!));
        return uses;
    }

    // ---- Unzipping

    /// <summary>Unzips the archived folder (its physical path), or joins the unzipping that runs already.</summary>
    public UnzipJob Unzip(string folderPath)
    {
        FolderState state = StateOf(folderPath);
        // While it is written it looks zipped (it isn't mounted): stopped, its copy is mounted again, and there is nothing left to unzip
        Volatile.Read(ref state.Zipping)?.StopBecauseOpened();
        lock (state)
        {
            if (state.Unzipping is { Finished.IsCompleted: false } running)
                return running;

            UnzipJob job = new();
            job.Finished = Task.Run(() => UnzipAsync(folderPath, state, job));
            state.Unzipping = job;
            return job;
        }
    }

    private async Task<string?> UnzipAsync(string folderPath, FolderState state, UnzipJob job)
    {
        await state.Gate.WaitAsync();
        try
        {
            if (MountedAt(folderPath) is not null)
                return null;

            ArchivedFolder folder = FolderOf(folderPath);
            // (A copy left by a crash, not mounted yet, is what the folder holds: it is mounted as it is)
            if (archiver.Recover(folder) != ArchivedFolderState.Unzipped)
            {
                // An encrypted archive needs the password of the folder that set it up, known since someone unlocked it
                string? password = null;
                if (ArchiveReading.IsEncrypted(folder.ArchivePath, folder.Format))
                {
                    string passwordFolder = resolver.Resolve(folderPath).SettingsFolder ?? Normalize(folderPath);
                    if (passwords.PasswordOf(passwordFolder) is not { } known)
                    {
                        job.NeedsPasswordOf = passwordFolder;
                        return $"The password of {passwordFolder} isn't known: it has to be unlocked first.";
                    }
                    password = known;
                }
                await archiver.UnzipAsync(folder, password, new Progress<double>(job.Report), lifetime.ApplicationStopping);
            }

            Mount(folderPath, folder.UnzippedDirectory, state);
            return null;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not unzip the archived folder {folder}", folderPath);
            return e.Message;
        }
        finally
        {
            _ = state.Gate.Release();
        }
    }

    private EntryLocation Mount(string folderPath, string unzippedPath, FolderState state)
    {
        EntryLocation location = locations.MountArchive(folderPath, unzippedPath,
            keepsHistory: (locations.LocationOf(folderPath) ?? locations.Root).KeepsHistory);
        history.ArchiveUnzipped(location);
        Interlocked.Exchange(ref state.LastUsedTicks, time.GetUtcNow().UtcTicks);
        logger.LogInformation("Unzipped the archived folder /{folder}", location.VirtualPath);
        return location;
    }

    // ---- Zipping

    /// <summary>
    /// Zips the unzipped folder (and those unzipped inside it, first) once nothing uses it, unless <paramref name="onlyIfIdle"/> and it is
    /// in use or was used within <see cref="ArchivesConfig.ZipAfterMinutes"/>. Whether it is zipped now. If zipping fails, the copy stays
    /// mounted (and counts): nothing is lost.
    /// </summary>
    /// <param name="stopsWhenOpened">
    /// Whether opening the folder (a request that needs what is in it) stops it, until the new archive took the old one's place: for zipping
    /// that only saves space (the idle ones, the archive button, the as-is download), not for a move or a delete that waits for it.
    /// </param>
    /// <param name="report">Also told how far it got (0 to 1), from the thread that zips.</param>
    public Task<bool> ZipAsync(string folderPath, bool onlyIfIdle = false, bool stopsWhenOpened = false, Action<double>? report = null)
        => ZipAsync(folderPath, onlyIfIdle, stopsWhenOpened, report, lifetime.ApplicationStopping);

    /// <param name="outer">Stops it with the zipping of the folder it is in (opening that one stops this one too).</param>
    private async Task<bool> ZipAsync(string folderPath, bool onlyIfIdle, bool stopsWhenOpened, Action<double>? report,
        CancellationToken outer)
    {
        FolderState state = StateOf(folderPath);
        if (onlyIfIdle)
        {
            if (!IsIdle(state) || !await state.Gate.WaitAsync(0))
                return false;
        }
        else
            await state.Gate.WaitAsync();

        using ZipJob job = new(stopsWhenOpened, outer) { AlsoReport = report };
        try
        {
            if (MountedAt(folderPath) is not { } location)
                return true;
            Volatile.Write(ref state.Zipping, job);

            // Those unzipped inside it first: their archives are part of this one
            foreach (EntryLocation inner in locations.UnzippedArchives.Where(other => other != location
                         && other.PhysicalPath.StartsWith(location.PhysicalPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(other => other.PhysicalPath.Length).ToList())
                if (!await ZipAsync(inner.MountPath!, onlyIfIdle, stopsWhenOpened, report: null, job.Token))
                    return false;

            // Nothing new starts to use it (the gate is held); what still does is waited for, unless it was only to be zipped while idle. Not
            // forever: a request that uses it may be the one that wants it zipped (a delete asked for from a page inside it).
            DateTime giveUp = DateTime.UtcNow + MaxWaitForUses;
            while (Volatile.Read(ref state.Users) > 0)
            {
                if (onlyIfIdle || DateTime.UtcNow > giveUp || job.Token.IsCancellationRequested)
                    return false;
                await Task.Delay(20);
            }
            if (job.Token.IsCancellationRequested)
                return false;

            return await ZipMountedAsync(folderPath, location, state, job);
        }
        finally
        {
            Volatile.Write(ref state.Zipping, null);
            _ = state.Gate.Release();
        }
    }

    private async Task<bool> ZipMountedAsync(string folderPath, EntryLocation location, FolderState state, ZipJob job)
    {
        // Written in the format and with the compression as configured now: an archive of the other format (the filetype changed) is
        // replaced by it (FolderArchiver deletes it once the new one is in place)
        ArchivedFolder existing = FolderOf(folderPath);
        ResolvedArchive resolved = resolver.Resolve(folderPath);
        ArchiveSettings archive = resolved.Settings ?? ArchiveSettings.Disabled with { Format = existing.Format };

        // Encrypted with the password of the folder that set it up, as long as it is the one its configuration has. Unknown (nobody unlocked
        // it since the app started): it stays unzipped until someone does, rather than being zipped without it
        string? password = null;
        if (archive.Encrypted)
        {
            string passwordFolder = resolved.SettingsFolder ?? Normalize(folderPath);
            password = passwords.PasswordOf(passwordFolder);
            if (password is null)
            {
                logger.LogWarning("The encrypted folder /{folder} stays unzipped until someone unlocks it: its password isn't known now",
                    location.VirtualPath);
                return false;
            }
        }

        string target = Path.Combine(Normalize(folderPath), ArchivedFolder.ArchiveFileName(archive.Format));
        ArchivedFolder folder = FolderOf(Normalize(folderPath), target) with
        {
            FormerArchivePath = ArchivedFolder.ArchivePathsIn(Normalize(folderPath))
                .FirstOrDefault(path => !string.Equals(path, target, StringComparison.OrdinalIgnoreCase) && File.Exists(path)),
            // An archive that wasn't encrypted (encryption was just turned on) is overwritten before it goes, not only deleted
            ShredsReplacedArchives = password is not null && ArchiveFileOf(folderPath) is { } old
                                     && !ArchiveReading.IsEncrypted(old, old.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                         ? ArchiveFormat.Zip : ArchiveFormat.SevenZip)
        };
        ArchiveCompression compression = folder.Format == ArchiveFormat.SevenZip && archive.Compression == ArchiveCompression.Store
            ? ArchiveCompression.Min
            : folder.Format == ArchiveFormat.Zip && archive.Compression == ArchiveCompression.Min ? ArchiveCompression.Store : archive.Compression;

        history.ArchiveZipping(location);
        locations.UnmountArchive(location);
        try
        {
            await archiver.ZipAsync(folder, new ArchiveOptions(folder.Format, compression, password), job.Token, job);
            logger.LogInformation("Zipped the archived folder /{folder}", location.VirtualPath);
            if (password is not null)
                ForgetPasswordIfUnneeded(resolved.SettingsFolder ?? Normalize(folderPath));
            return true;
        }
        catch (Exception e)
        {
            // The copy is still there and counts (FolderArchiver's promise): it is used as before
            if (e is OperationCanceledException && job.Token.IsCancellationRequested && !lifetime.ApplicationStopping.IsCancellationRequested)
                logger.LogInformation("Stopped zipping the archived folder /{folder}, as it was opened; it stays unzipped", location.VirtualPath);
            else
                logger.LogError(e, "Could not zip the archived folder {folder}; it stays unzipped", folderPath);
            if (Directory.Exists(folder.UnzippedDirectory))
                _ = Mount(folderPath, folder.UnzippedDirectory, state);
            return false;
        }
    }

    /// <summary>
    /// Forgets the password of the folder (that sets up encrypted archives) once no unzipped folder needs it to be zipped again: it is only
    /// in memory while it has to be.
    /// </summary>
    private void ForgetPasswordIfUnneeded(string passwordFolder)
    {
        bool stillNeeded = UnzippedFolders().Any(folder => resolver.Resolve(folder) is { Settings.Encrypted: true } resolved
                                                           && IsSame(resolved.SettingsFolder!, passwordFolder));
        if (!stillNeeded)
            passwords.Forget(passwordFolder);
    }

    private bool IsIdle(FolderState state)
        => Volatile.Read(ref state.Users) == 0
           && time.GetUtcNow().UtcTicks - Interlocked.Read(ref state.LastUsedTicks) >= TimeSpan.FromMinutes(settings.ZipAfterMinutes).Ticks;

    /// <summary>
    /// Zips every unzipped folder that is the entry (physical path, as a request has it) or inside it, waiting for what uses them: before the
    /// entry is renamed, as their copies are mounted where the entry is now. Whether all of them are zipped now.
    /// </summary>
    public async Task<bool> ZipAtOrBelowAsync(string physicalPath)
    {
        bool all = true;
        foreach (EntryLocation archive in locations.UnzippedArchives
                     .Where(archive => locations.IsSameOrInside(archive.PhysicalPath, physicalPath))
                     .OrderByDescending(archive => archive.PhysicalPath.Length).ToList())
            all &= MountedAt(archive.MountPath!) is null || await ZipAsync(archive.MountPath!);
        return all;
    }

    /// <summary>The archived folders that are unzipped now, by their physical path, the innermost first (the order they are zipped in).</summary>
    public List<string> UnzippedFolders()
        => [.. locations.UnzippedArchives.OrderByDescending(archive => archive.PhysicalPath.Length).Select(archive => archive.MountPath!)];

    /// <summary>Zips every unzipped folder that wasn't used for <see cref="ArchivesConfig.ZipAfterMinutes"/> (a background job).</summary>
    public async Task<int> ZipIdleAsync()
    {
        int zipped = 0;
        // The innermost first: an outer one is only idle once those inside it are zipped (or idle too)
        foreach (EntryLocation archive in locations.UnzippedArchives.OrderByDescending(archive => archive.PhysicalPath.Length).ToList())
            if (MountedAt(archive.MountPath!) is not null && await ZipAsync(archive.MountPath!, onlyIfIdle: true, stopsWhenOpened: true))
                zipped++;
        return zipped;
    }


    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSame(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string inner, string outer)
        => IsSame(inner, outer) || Normalize(inner).StartsWith(Normalize(outer) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private sealed class Use(Action end) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
                end();
        }
    }

    /// <summary>Several uses, given back together.</summary>
    private sealed class Uses : IDisposable
    {
        private readonly List<IDisposable> _uses = [];

        public void Add(IDisposable use) => _uses.Add(use);

        public void Dispose()
        {
            foreach (IDisposable use in _uses)
                use.Dispose();
            _uses.Clear();
        }
    }
}
