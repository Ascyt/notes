using System.Collections.Concurrent;
using Notes.Features.Bridges;

namespace Notes.Features.Components.Archive;

/// <summary>An archive that was made for a user and waits to be downloaded by them.</summary>
/// <param name="PhysicalPath">The archive file, in the app's data.</param>
/// <param name="FileName">The name it is downloaded as.</param>
/// <param name="Prepared">When it was finished (by the app's clock).</param>
/// <param name="Number">Counts up with every archive, so that the newest are known even where two were finished at the same time.</param>
public sealed record PreparedArchive(Guid Id, string UserId, string PhysicalPath, string FileName, DateTimeOffset Prepared, long Number);

/// <summary>
/// The archives that were made to be downloaded (<see cref="ArchiveHandler"/>): the files in the temporary folder of the app's data at the
/// root, and who they belong to in memory. A restart forgets who they belong to; the files are then left to
/// <see cref="DeleteStale"/>. Also limits how many are made at once, as compressing takes processors and memory.
/// </summary>
public sealed class PreparedArchives(EntryLocations locations, TimeProvider time)
{
    /// <summary>A user downloads one archive at a time; older ones are deleted to make room for new ones beyond this.</summary>
    public const int MaxKeptPerUser = 5;

    /// <summary>Archives made at the same time, for everyone together; the others wait for their turn.</summary>
    private readonly SemaphoreSlim _makingSlots = new(Math.Max(1, Environment.ProcessorCount / 2));

    private readonly ConcurrentDictionary<Guid, PreparedArchive> _archives = new();

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

    /// <summary>Waits until an archive may be made (see <see cref="_makingSlots"/>); disposing what it returns ends the turn.</summary>
    public async Task<IDisposable> WaitForTurnAsync(CancellationToken cancellationToken)
    {
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
