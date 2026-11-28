using System.Collections.Concurrent;
using Notes.Features.Bridges;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Archive;

/// <summary>An archive that was made for a user and waits to be downloaded by them.</summary>
/// <param name="PhysicalPath">The archive file, in the app's data.</param>
/// <param name="FileName">The name it is downloaded as.</param>
/// <param name="Prepared">When it was finished (by the app's clock).</param>
/// <param name="Number">Counts up with every archive, so that the newest are known even where two were finished at the same time.</param>
public sealed record PreparedArchive(Guid Id, string UserId, string PhysicalPath, string FileName, DateTimeOffset Prepared, long Number);

/// <summary>How the making of an archive ended: with the archive, or why not (the status and text of the answer).</summary>
public sealed record ArchiveJobResult(PreparedArchive? Archive, bool LeftOutUnreadable, int FailureStatus = 0, string? Failure = null);

/// <summary>
/// An archive being made, apart from the request that asked for it: a big one takes longer than a proxy waits for an answer. The page asks
/// how far it got (<see cref="Progress"/>) until it is <see cref="Finished"/>. Nobody asking for a while (the page was left) cancels it.
/// </summary>
public sealed class ArchiveJob
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _abandonAfter;
    private double _progress;
    private WorkStatus _status = new(WorkPhases.Waiting);
    private int _waiting;
    private long _lastAskedTicks;

    internal ArchiveJob(Guid id, string userId, TimeProvider time, TimeSpan abandonAfter, CancellationToken stopping)
    {
        Id = id;
        UserId = userId;
        _time = time;
        _abandonAfter = abandonAfter;
        Started = time.GetUtcNow();
        _lastAskedTicks = Started.UtcTicks;
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    }

    public Guid Id { get; }
    public string UserId { get; }
    public DateTimeOffset Started { get; }

    /// <summary>How far it got, 0 to 1 (1 only once it is finished).</summary>
    public double Progress => Finished.IsCompletedSuccessfully ? 1 : Volatile.Read(ref _progress);

    /// <summary>What it is doing now, for the text under its bar (waiting for its turn, at first).</summary>
    public WorkStatus Status => Volatile.Read(ref _status);

    internal void SetStatus(WorkStatus status) => Volatile.Write(ref _status, status);

    public Task<ArchiveJobResult> Finished { get; internal set; } = Task.FromResult(new ArchiveJobResult(null, false));

    internal CancellationTokenSource Cancellation { get; }

    /// <summary>The page asked how far it got: it still waits for it.</summary>
    public void Asked() => Interlocked.Exchange(ref _lastAskedTicks, _time.GetUtcNow().UtcTicks);

    /// <summary>A request waits for it to finish (one that didn't ask for an answer right away); dispose when it stops waiting.</summary>
    public IDisposable Waiting()
    {
        _ = Interlocked.Increment(ref _waiting);
        return new Ending(() => Interlocked.Decrement(ref _waiting));
    }

    /// <summary>Told by the writer how far it got. Cancels it if nobody waits for it any more.</summary>
    internal void Report(double progress)
    {
        Volatile.Write(ref _progress, progress);
        if (IsAbandoned)
            Cancellation.Cancel();
    }

    /// <summary>Whether nobody waits for it any more: no request, and the page hasn't asked about it for a while.</summary>
    internal bool IsAbandoned => Volatile.Read(ref _waiting) == 0
        && _time.GetUtcNow().UtcTicks - Interlocked.Read(ref _lastAskedTicks) > _abandonAfter.Ticks;

    private sealed class Ending(Action end) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
                end();
        }
    }
}

/// <summary>
/// The archives that were made to be downloaded (<see cref="ArchiveHandler"/>): the files in the temporary folder of the app's data at the
/// root, and who they belong to in memory. A restart forgets who they belong to; the files are then left to
/// <see cref="DeleteStale"/>. Also limits how many are made at once, as compressing takes processors and memory, and keeps the ones being
/// made (<see cref="ArchiveJob"/>).
/// </summary>
public sealed class PreparedArchives(EntryLocations locations, TimeProvider time, IHostApplicationLifetime lifetime,
    Operations.StorageOperations? operations = null)
{
    /// <summary>A user downloads one archive at a time; older ones are deleted to make room for new ones beyond this.</summary>
    public const int MaxKeptPerUser = 5;

    /// <summary>How long an archive is made without the page asking how far it got, before it counts as no longer wanted.</summary>
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(1);

    /// <summary>Archives made at the same time, for everyone together; the others wait for their turn.</summary>
    private readonly SemaphoreSlim _makingSlots = new(Math.Max(1, Environment.ProcessorCount / 2));

    private readonly ConcurrentDictionary<Guid, PreparedArchive> _archives = new();

    private readonly ConcurrentDictionary<Guid, ArchiveJob> _jobs = new();

    /// <summary>The users of whom an archive is being made: one at a time for each.</summary>
    private readonly ConcurrentDictionary<string, byte> _making = new();

    private long _lastNumber;

    private string Directory => locations.TempDirectoryFor(locations.Root.PhysicalPath, "archive");

    /// <summary>
    /// Claims the right to make an archive for the user, or returns null if one is being made for them already. Disposing the claim gives it
    /// back.
    /// </summary>
    public IDisposable? TryClaimMaking(string userId)
        => _making.TryAdd(userId, 0) ? new MakingClaim(this, userId) : null;

    /// <summary>
    /// Starts making an archive, apart from the request: <paramref name="work"/> runs on its own and is given the job (to report how far it
    /// got) and what cancels it (the app stopping, or nobody waiting any more). The claim is given back once it is finished.
    /// </summary>
    /// <param name="physicalPath">What it is of (the directory the request is for), for the list of operations.</param>
    public ArchiveJob Start(Guid id, string userId, IDisposable claim, Func<ArchiveJob, CancellationToken, Task<ArchiveJobResult>> work,
        string physicalPath = "")
    {
        ArchiveJob job = new(id, userId, time, AbandonAfter, lifetime.ApplicationStopping);
        _jobs[id] = job;
        // (In the list of operations while it runs; who started it is the user of this request)
        IDisposable? operation = operations?.Begin(Operations.OperationKind.PreparingDownload, physicalPath, () => job.Progress, () => job.Status,
            () => job.Cancellation.Cancel());
        job.Finished = Task.Run(async () =>
        {
            try
            {
                return await work(job, job.Cancellation.Token);
            }
            finally
            {
                operation?.Dispose();
                claim.Dispose();
                job.Cancellation.Dispose();
            }
        });
        return job;
    }

    /// <summary>The user's job with that id (being made, or finished within the hour), or null.</summary>
    public ArchiveJob? FindJob(string userId, Guid id)
        => _jobs.TryGetValue(id, out ArchiveJob? job) && job.UserId == userId ? job : null;

    /// <summary>Waits until an archive may be made (see <see cref="_makingSlots"/>); disposing what it returns ends the turn.</summary>
    /// <param name="job">Told, while it waits, which one it waits for.</param>
    public async Task<IDisposable> WaitForTurnAsync(CancellationToken cancellationToken, ArchiveJob? job = null)
    {
        if (job is not null && _makingSlots.CurrentCount == 0)
            job.SetStatus(new WorkStatus(WorkPhases.Waiting,
                WaitingFor: operations?.WaitingFor(Operations.OperationKind.PreparingDownload, other => other.Started >= job.Started)));
        await _makingSlots.WaitAsync(cancellationToken);
        return new Turn(_makingSlots);
    }

    /// <summary>Where a new archive is to be written. The folder is there afterwards.</summary>
    public string NewPath(Guid id, string extension)
    {
        _ = System.IO.Directory.CreateDirectory(Directory);
        return Path.Combine(Directory, id.ToString("N") + extension);
    }

    /// <summary>Keeps the finished archive for the user, deleting their oldest ones beyond <see cref="MaxKeptPerUser"/>.</summary>
    public PreparedArchive Add(Guid id, string userId, string physicalPath, string fileName)
    {
        DateTimeOffset now = time.GetUtcNow();
        // Its age, for DeleteStale, by the app's clock (the file system's own time would be another clock, as far as the tests are concerned)
        File.SetLastWriteTimeUtc(physicalPath, now.UtcDateTime);
        PreparedArchive archive = new(id, userId, physicalPath, fileName, now, Interlocked.Increment(ref _lastNumber));
        _archives[id] = archive;

        foreach (PreparedArchive old in _archives.Values.Where(other => other.UserId == userId).OrderByDescending(other => other.Number)
                     .Skip(MaxKeptPerUser))
            Remove(old);

        return archive;
    }

    /// <summary>The user's archive with that id, or null if they have none such (another user's is none of theirs).</summary>
    public PreparedArchive? Find(string userId, Guid id)
        => _archives.TryGetValue(id, out PreparedArchive? archive) && archive.UserId == userId && File.Exists(archive.PhysicalPath)
            ? archive
            : null;

    /// <summary>
    /// Deletes the archives finished more than <paramref name="maxAge"/> ago, and every file in the folder that is that old (also those a
    /// restart left without an owner, or of a making that was cut off). Returns how many files were deleted.
    /// </summary>
    public int DeleteStale(TimeSpan maxAge)
    {
        DateTimeOffset cutoff = time.GetUtcNow() - maxAge;
        foreach (PreparedArchive archive in _archives.Values.Where(archive => archive.Prepared < cutoff))
            _ = _archives.TryRemove(archive.Id, out _);
        foreach (ArchiveJob job in _jobs.Values.Where(job => job.Finished.IsCompleted && job.Started < cutoff))
            _ = _jobs.TryRemove(job.Id, out _);

        if (!System.IO.Directory.Exists(Directory))
            return 0;

        int deleted = 0;
        foreach (string file in System.IO.Directory.EnumerateFiles(Directory))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff.UtcDateTime)
                    continue;

                File.Delete(file);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Being downloaded right now, or already gone; the next run tries again
            }
        }

        return deleted;
    }

    private void Remove(PreparedArchive archive)
    {
        _ = _archives.TryRemove(archive.Id, out _);
        try
        {
            File.Delete(archive.PhysicalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Being downloaded right now: DeleteStale gets it later
        }
    }

    private sealed class MakingClaim(PreparedArchives archives, string userId) : IDisposable
    {
        public void Dispose() => archives._making.TryRemove(userId, out _);
    }

    private sealed class Turn(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _ = slots.Release();
        }
    }
}
