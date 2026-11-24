using System.Collections.Concurrent;
using Notes.Features.Bridges;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.ZippedFolders;

/// <summary>An archived folder being unzipped: how far it got, and how it ended.</summary>
public sealed class UnzipJob
{
    private double _progress;

    /// <summary>How far it got, 0 to 1.</summary>
    public double Progress => Volatile.Read(ref _progress);

    internal void Report(double progress) => Volatile.Write(ref _progress, progress);

    /// <summary>Done once it is unzipped (and mounted). Null if it worked, otherwise why not (for the log; the user is told less).</summary>
    public Task<string?> Finished { get; internal set; } = Task.FromResult<string?>(null);
}

/// <summary>
/// An archived folder being zipped (from when zipping takes its gate, waiting for what uses it included): how far it got, and the stop that
/// opening it pulls, where it may.
/// </summary>
internal sealed class ZipJob(bool stopsWhenOpened, CancellationToken outer) : IProgress<double>, IDisposable
{
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(outer);
    private double _progress;

    /// <summary>Told how far it got (from the thread that zips), and passed on to who else waits for it.</summary>
    public Action<double>? AlsoReport { get; init; }

    public double Progress => Volatile.Read(ref _progress);

    public CancellationToken Token => _stop.Token;

    public void Report(double value)
    {
        Volatile.Write(ref _progress, value);
        AlsoReport?.Invoke(value);
    }

    /// <summary>
    /// Stops it, if it may be stopped for that (not a zipping that a move or a delete waits for). Past the point where the new archive took
    /// the old one's place this changes nothing: it finishes.
    /// </summary>
    public void StopBecauseOpened()
    {
        if (!stopsWhenOpened || _stop.IsCancellationRequested)
            return;
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ended meanwhile
        }
    }

    public void Dispose() => _stop.Dispose();
}

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
/// </remarks>
public sealed class ArchivedFolders(EntryLocations locations, IPathService paths, FolderConfigStore configs, FolderArchiver archiver,
    HistoryService history, ArchivesConfig settings, TimeProvider time, IHostApplicationLifetime lifetime, ILogger<ArchivedFolders> logger)
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
    /// Whether the folder (its physical path, or that of its unzipped copy) is archived, as its configuration says now. One whose archiving
    /// was just turned on may not be converted yet.
    /// </summary>
    public bool IsArchived(string physicalDirectory)
        => configs.Get(physicalDirectory).Config?.Archive.Enabled == true;

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
    /// <exception cref="IOException">If one can't be zipped now (something in it is in use): nothing was done then.</exception>
    public async Task<T> WhileZippedAsync<T>(IReadOnlyList<string> physicalPaths, Func<IReadOnlyList<string>, Task<T>> action)
    {
        List<string> outer = [.. physicalPaths.Select(OuterOf)];
        for (;;)
        {
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

    /// <summary>The archive of the folder that is there on disk (<c>_.7z</c> or <c>_.zip</c>), or null.</summary>
    public static string? ArchiveFileOf(string folderPath)
        => new[] { ArchiveFormat.SevenZip, ArchiveFormat.Zip }.Select(format => Path.Combine(folderPath, ArchivedFolder.ArchiveFileName(format)))
            .FirstOrDefault(File.Exists);

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

    /// <summary>Where the folder's archive, its unzipped copy and the work directory of zipping are.</summary>
    private ArchivedFolder FolderOf(string folderPath)
    {
        string folder = Normalize(folderPath);
        // In the data of the location the folder is in (for one inside another archived folder: in that one's unzipped copy)
        EntryLocation location = locations.LocationOf(folder) ?? locations.Root;
        string relative = Path.GetRelativePath(location.PhysicalPath, folder);
        ArchiveFormat format = ArchiveFileOf(folder) is { } existing && existing.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? ArchiveFormat.Zip
            : ArchiveFileOf(folder) is not null ? ArchiveFormat.SevenZip : configs.Get(folder).Config?.Archive.Format ?? ArchiveFormat.SevenZip;
        return new ArchivedFolder(Path.Combine(folder, ArchivedFolder.ArchiveFileName(format)),
            Path.Combine(location.DataDirectory, "temp", UnzippedKind, relative),
            Path.Combine(location.DataDirectory, "temp", ZippingKind));
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
                await archiver.UnzipAsync(folder, password: null, new Progress<double>(job.Report), lifetime.ApplicationStopping);

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
        ArchivedFolder folder = FolderOf(folderPath);
        ArchiveSettings archive = configs.Get(folderPath).Config?.Archive ?? ArchiveSettings.Disabled;
        // The format the archive has (changing it is for later); the compression as configured now
        ArchiveCompression compression = folder.Format == ArchiveFormat.SevenZip && archive.Compression == ArchiveCompression.Store
            ? ArchiveCompression.Min
            : folder.Format == ArchiveFormat.Zip && archive.Compression == ArchiveCompression.Min ? ArchiveCompression.Store : archive.Compression;

        history.ArchiveZipping(location);
        locations.UnmountArchive(location);
        try
        {
            await archiver.ZipAsync(folder, new ArchiveOptions(folder.Format, compression, Password: null), job.Token, job);
            logger.LogInformation("Zipped the archived folder /{folder}", location.VirtualPath);
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
    private static void PutBackConfiguration(string folder, string unzipped)
    {
        _ = Directory.CreateDirectory(folder);
        string inCopy = Path.Combine(unzipped, PathService.ConfigFileName);
        string outside = Path.Combine(folder, PathService.ConfigFileName);
        if (File.Exists(inCopy) && !File.Exists(outside))
            File.Move(inCopy, outside);
        DurableFiles.FlushDirectory(folder);
    }

    // ---- After a crash, when the app starts

    /// <summary>
    /// Finds what zipping, unzipping or turning archiving on left when the app stopped, in every location, and puts it right: half done work
    /// is deleted, copies that are there are mounted (they hold everything), and a folder that was being turned into an archived one gets its
    /// configuration back. Folders whose archiving is on but that weren't converted yet are converted. Runs before the first request.
    /// </summary>
    public async Task RecoverAsync()
    {
        foreach (EntryLocation location in locations.All.Where(location => !location.IsArchive).ToList())
            await RecoverInAsync(location);
    }

    private async Task RecoverInAsync(EntryLocation location)
    {
        string data = location.DataDirectory;
        _ = archiver.RecoverWorkDirectory(Path.Combine(data, "temp", ZippingKind));

        // Copies whose folder is gone: the app stopped while a folder was turned into an archived one
        string unzippedRoot = Path.Combine(data, "temp", UnzippedKind);
        if (Directory.Exists(unzippedRoot))
            foreach (string copy in CopyRootsIn(location, unzippedRoot))
            {
                string folder = Path.Combine(location.PhysicalPath, Path.GetRelativePath(unzippedRoot, copy));
                if (!Directory.Exists(folder))
                {
                    logger.LogWarning("{copy} was being made an archived folder when the app stopped; it is put back at {folder}", copy, folder);
                    PutBackConfiguration(folder, copy);
                }
            }

        foreach (string folder in ArchivedFoldersIn(location.PhysicalPath))
        {
            ArchivedFolder archived = FolderOf(folder);
            switch (archiver.Recover(archived))
            {
                case ArchivedFolderState.Unzipped:
                    logger.LogWarning("The archived folder {folder} was unzipped when the app stopped; its copy counts, and is zipped later", folder);
                    EntryLocation mounted = Mount(folder, archived.UnzippedDirectory, StateOf(folder));
                    await RecoverInAsync(mounted);
                    break;
                case ArchivedFolderState.Missing:
                    // Turned on, but not converted yet (the app stopped right after the configuration was saved)
                    await ConvertAsync(folder);
                    break;
            }
        }
    }

    /// <summary>The unzipped copies in the folder of copies: the directories whose folder is archived, or is gone (but for its copy).</summary>
    private IEnumerable<string> CopyRootsIn(EntryLocation location, string unzippedRoot)
    {
        Stack<string> pending = new([unzippedRoot]);
        while (pending.TryPop(out string? directory))
        {
            foreach (string inner in Directory.EnumerateDirectories(directory))
            {
                string folder = Path.Combine(location.PhysicalPath, Path.GetRelativePath(unzippedRoot, inner));
                bool isCopy = Directory.Exists(folder) ? IsArchived(folder) : File.Exists(Path.Combine(inner, PathService.ConfigFileName));
                if (isCopy)
                    yield return inner;
                else if (Directory.Exists(folder))
                    pending.Push(inner);
            }
        }
    }

    /// <summary>The archived folders in the directory (not its own data, nor what a bridge hides or what is inside archived ones).</summary>
    private List<string> ArchivedFoldersIn(string root)
    {
        List<string> found = [];
        Stack<string> pending = new([root]);
        EnumerationOptions options = new() { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        while (pending.TryPop(out string? directory))
        {
            foreach (string inner in Directory.EnumerateDirectories(directory, "*", options))
            {
                if ((IsSame(directory, root) && Path.GetFileName(inner) == PathService.ReservedName) || locations.IsHiddenByBridge(inner))
                    continue;
                if (IsArchived(inner))
                    found.Add(inner);
                else
                    pending.Push(inner);
            }
        }
        return found;
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
