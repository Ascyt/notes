using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;
using Notes.Features.ZippedFolders;

namespace Notes.Features.Components.Upload;

/// <summary>How unzipping an uploaded archive ended (<see cref="UploadedArchiveUnzipper"/>).</summary>
/// <param name="FolderVirtualPath">The folder it went into; null if it failed (or was given up: the archive was gone).</param>
/// <param name="LeftOut">The entries that were left out, as they can't be entries here (see <see cref="UploadedArchives.IsLeftOut"/>).</param>
/// <param name="Error">Why it failed, or null.</param>
public sealed record UnzipOutcome(string? FolderVirtualPath, int LeftOut, string? Error);

/// <summary>
/// Unzips uploaded archives in the background, one after the other, each into a new folder next to it named after it ("name (n)" if taken),
/// which then takes its place. The upload itself is finished by then: the archive is a file like any other, whose entry in the configuration
/// says that it is to be unzipped (<see cref="FileEntry.UnzipPending"/>), so the listing shows a bar for it, and a restart loses nothing: the
/// listing of its folder starts it again (<see cref="EnsureRunning"/>). What fails leaves the archive as it is, with the reason in its entry
/// (<see cref="FileEntry.UnzipError"/>), and is not tried again.
/// </summary>
/// <remarks>
/// It is unzipped into the app's data of the location (the same volume), without the <see cref="StructureLock"/>, and only put in place under
/// it, in one move: nothing half unzipped is ever an entry. If the archive is gone by then (renamed, moved, deleted), or its entry no longer
/// says it is to be unzipped (an admin removed that), what was unzipped is thrown away. Who uploaded it was checked when it was uploaded (that
/// they may create the folder); the folder is not checked again, nor the number its name may get.
/// </remarks>
public sealed class UploadedArchiveUnzipper(EntryLocations locations, IPathService path, StructureLock structureLock, FolderConfigStore configs,
    LimitsConfig limits, UploadTexts texts, NameTexts nameTexts, HistoryService history, ILogger<UploadedArchiveUnzipper> logger,
    IHostApplicationLifetime lifetime, ArchivedFolders? archives = null)
{
    public const string StagingKind = "unzip";

    /// <summary>How long it is remembered how one ended, for the bar that asks about it.</summary>
    private static readonly TimeSpan KeepOutcomes = TimeSpan.FromMinutes(10);

    private sealed class Job(string physicalPath, ClaimsPrincipal user)
    {
        public string PhysicalPath { get; } = physicalPath;
        public ClaimsPrincipal User { get; } = user;
        private double _progress;
        public double Progress
        {
            get => Volatile.Read(ref _progress);
            set => Volatile.Write(ref _progress, value);
        }
    }

    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (UnzipOutcome Outcome, DateTime At)> _outcomes = new(StringComparer.OrdinalIgnoreCase);

    // One at a time: unzipping is mostly the disk's work, two at once only take longer each
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>
    /// Unzips the archive (its physical path, whose entry says it is to be unzipped) soon, unless that is under way already. The history
    /// records the change as <paramref name="user"/>'s (the app's if null: after a restart).
    /// </summary>
    public void Start(string physicalPath, ClaimsPrincipal? user)
    {
        Job job = new(physicalPath, user ?? new ClaimsPrincipal());
        if (_jobs.TryAdd(physicalPath, job))
            _ = Task.Run(() => RunAsync(job));
    }

    /// <summary>For the listing, which shows a bar for an archive whose entry says it is to be unzipped: starts it if it doesn't run (after a restart).</summary>
    public void EnsureRunning(string physicalPath)
    {
        if (!_jobs.ContainsKey(physicalPath))
            Start(physicalPath, user: null);
    }

    /// <summary>How far unzipping it got (0 to 1), or null if it isn't being unzipped.</summary>
    public double? ProgressOf(string physicalPath)
        => _jobs.TryGetValue(physicalPath, out Job? job) ? job.Progress : null;

    /// <summary>How unzipping it ended lately, if it did.</summary>
    public UnzipOutcome? OutcomeOf(string physicalPath)
        => _outcomes.TryGetValue(physicalPath, out var outcome) ? outcome.Outcome : null;

    /// <summary>
    /// Deletes what unzipping left in the app's data of every location when the app stopped: unzipping starts over (the archive is still
    /// there, and its entry still says it is to be unzipped). When the app starts, before anything is unzipped.
    /// </summary>
    public void ClearLeftovers()
    {
        foreach (EntryLocation location in locations.All)
        {
            string staging = Path.Combine(location.DataDirectory, "temp", StagingKind);
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Could not delete what unzipping left in {staging}", staging);
            }
        }
    }

    private async Task RunAsync(Job job)
    {
        CancellationToken stopping = lifetime.ApplicationStopping;
        UnzipOutcome? outcome = null;
        try
        {
            await _oneAtATime.WaitAsync(stopping);
            try
            {
                // (The archived folder it is in, if any, isn't zipped meanwhile: the archive is in its unzipped copy)
                using IDisposable uses = archives is null ? new NoUses() : await archives.UseAllAroundAsync([job.PhysicalPath]);
                outcome = await UnzipAsync(job, stopping);
            }
            finally
            {
                _ = _oneAtATime.Release();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The app stops: its entry still says it is to be unzipped, which starts it again
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unzipping the uploaded archive {path} failed", job.PhysicalPath);
        }
        finally
        {
            if (outcome is not null)
                Remember(job.PhysicalPath, outcome);
            _ = _jobs.TryRemove(job.PhysicalPath, out _);
        }
    }

    private async Task<UnzipOutcome?> UnzipAsync(Job job, CancellationToken stopping)
    {
        string name = Path.GetFileName(job.PhysicalPath);
        string directory = Path.GetDirectoryName(job.PhysicalPath)!;
        if (!File.Exists(job.PhysicalPath) || !IsPending(directory, name))
            return null;

        string staging = Path.Combine(locations.TempDirectoryFor(directory, StagingKind), Guid.NewGuid().ToString("N"));
        string? error;
        try
        {
            if (UploadedArchives.FormatOf(name) is not { } format)
                error = texts.NotAnArchive;
            else if (await Task.Run(() => UploadedArchives.WhyNotUnzippable(job.PhysicalPath, format, name, texts), stopping) is { } why)
                error = why;
            else
            {
                long declared = await Task.Run(() => UploadedArchives.DeclaredSize(job.PhysicalPath, format), stopping);
                int leftOut = await Task.Run(() => UploadedArchives.Extract(job.PhysicalPath, format, staging, name, limits, texts, nameTexts,
                    stopping, written => job.Progress = declared == 0 ? 0 : Math.Min(0.99, (double)written / declared)), stopping);
                return await PutInPlaceAsync(job, staging, leftOut);
            }
        }
        catch (UploadRefusedException e)
        {
            error = e.Message;
        }
        catch (ArchiveUnreadableException)
        {
            error = texts.ArchiveDamaged(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Unzipping the uploaded archive {path} failed", job.PhysicalPath);
            error = texts.UnzipFailed(name);
        }
        finally
        {
            DeleteStaging(staging);
        }

        await MarkFailedAsync(job, error);
        return new UnzipOutcome(null, 0, error);
    }

    /// <summary>
    /// Puts what was unzipped in place of the archive, under the lock, as an upload puts its file: with the next free "name (n)". Null if the
    /// archive is gone, or no longer to be unzipped.
    /// </summary>
    private async Task<UnzipOutcome?> PutInPlaceAsync(Job job, string staging, int leftOut)
    {
        string name = Path.GetFileName(job.PhysicalPath);
        string folderName = FolderNameOf(name);
        // What the archive holds goes in, without the one folder around all of it (photos.zip with photos/ in it is photos, not photos/photos)
        string contents = UploadedArchives.ContentsOf(staging);
        // The longest path in it, to check the paths it gets (relative, from the folder on)
        int longestInside = Directory.EnumerateFileSystemEntries(contents, "*", SearchOption.AllDirectories)
            .Select(entry => Encoding.UTF8.GetByteCount("/" + Path.GetRelativePath(contents, entry).Replace('\\', '/')))
            .DefaultIfEmpty(0).Max();

        string archivePath;
        string finalPath;
        using (await structureLock.EnterAsync())
        {
            // The folder may have been made an archived folder meanwhile: then the archive is in its unzipped copy now
            string directory = archives?.WhereItIsNow(Path.GetDirectoryName(job.PhysicalPath)!) ?? Path.GetDirectoryName(job.PhysicalPath)!;
            archivePath = Path.Combine(directory, name);
            if (!File.Exists(archivePath) || !IsPending(directory, name))
                return null;

            string finalName = folderName;
            for (int n = 1; ; n++)
            {
                finalPath = Path.Combine(directory, finalName);
                string? tooLong = Encoding.UTF8.GetByteCount(finalName) > NewNames.MaxNameBytes
                    ? texts.NumberedNameTooLong(folderName, NewNames.MaxNameBytes)
                    : Encoding.UTF8.GetByteCount(locations.ToVirtualPath(finalPath)) + longestInside > limits.MaxPathBytes
                        ? nameTexts.PathTooLong(path.ToVirtualPath(finalPath) + "/…", limits.MaxPathBytes)
                        : null;
                if (tooLong is not null)
                    throw new UploadRefusedException(StatusCodes.Status400BadRequest, tooLong);

                if (locations.IsFree(finalPath))
                {
                    try
                    {
                        // (What is in it has no configuration of its own: it has the permissions of the folder it is in)
                        Directory.Move(contents, finalPath);
                        break;
                    }
                    catch (IOException) when (Path.Exists(finalPath))
                    {
                        // Made outside of the app in the meantime: on to the next name
                    }
                }

                finalName = $"{folderName} ({n})";
            }

            File.Delete(archivePath);
            configs.FileRemoved(directory, name);
            // Below an onLowestLevel folder, the folders that hold files now are archived right away
            if (archives is not null)
                await archives.ArchiveLowestLevelAfterChangeAsync(finalPath);
        }

        history.AfterChange(job.User, [archivePath, finalPath, configs.ConfigPathOf(Path.GetDirectoryName(archivePath)!)],
            HistoryChangeKind.Change);
        return new UnzipOutcome(path.ToVirtualPath(finalPath), leftOut, null);
    }

    /// <summary>Its entry says why it failed (if it is still there and to be unzipped).</summary>
    private async Task MarkFailedAsync(Job job, string error)
    {
        string name = Path.GetFileName(job.PhysicalPath);
        string configPath;
        using (await structureLock.EnterAsync())
        {
            string directory = archives?.WhereItIsNow(Path.GetDirectoryName(job.PhysicalPath)!) ?? Path.GetDirectoryName(job.PhysicalPath)!;
            if (!File.Exists(Path.Combine(directory, name)) || !IsPending(directory, name))
                return;
            configs.UnzipFailed(directory, name, error);
            configPath = configs.ConfigPathOf(directory);
        }
        history.AfterChange(job.User, [configPath], HistoryChangeKind.Change);
    }

    private bool IsPending(string physicalDirectory, string name)
        => configs.Get(physicalDirectory).Config?.FindFile(name)?.UnzipPending == true;

    /// <summary>The name of the folder an archive is unzipped into: its own without the extension (or with it, if that leaves nothing).</summary>
    public static string FolderNameOf(string archiveName)
        => Path.GetFileNameWithoutExtension(archiveName) is var stem && PathService.IsValid(stem) ? stem : archiveName + " unzipped";

    private void Remember(string physicalPath, UnzipOutcome outcome)
    {
        DateTime now = DateTime.UtcNow;
        foreach ((string key, (UnzipOutcome _, DateTime at)) in _outcomes)
            if (now - at > KeepOutcomes)
                _ = _outcomes.TryRemove(key, out _);
        _outcomes[physicalPath] = (outcome, now);
    }

    private void DeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Deleted at the next start (ClearLeftovers)
            logger.LogWarning(e, "Could not delete {staging}", staging);
        }
    }

    private sealed class NoUses : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
