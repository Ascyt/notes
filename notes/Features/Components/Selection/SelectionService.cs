using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace notes.Features.Components.Selection;

/// <summary>What a user has selected, as the pages see it.</summary>
/// <param name="Version">Changes with every change of the selection, so a page can tell that it is showing an outdated one.</param>
/// <param name="Busy">Whether a copy or move of the selection is running.</param>
/// <param name="Paths">Virtual paths of the selected files and directories.</param>
/// <param name="DirectoryCount">How many of the <paramref name="Paths"/> are directories (the rest are files).</param>
/// <param name="CanDelete">Whether the user may delete (and so move away) every selected entry itself, as far as was known when it was selected. Not what is inside a directory: that is up to the transfer.</param>
/// <param name="Progress">While <paramref name="Busy"/>: how much of the transfer is done, from 0 to 1. Otherwise null.</param>
public sealed record SelectionSnapshot(long Version, bool Busy, IReadOnlyList<string> Paths, int DirectoryCount, bool CanDelete, double? Progress = null)
{
    public static SelectionSnapshot Empty { get; } = new(0, false, [], 0, true);
}

/// <summary>
/// An entry to select: its virtual path, whether it is a directory, and what was found out about its permissions when it was selected.
/// That is only for showing what can be done with the selection (and only about the entry itself, not what is inside it); whatever is done
/// with it checks everything again.
/// </summary>
public sealed record SelectionEntry(string Path, bool IsDirectory, bool CanDelete);

public enum TransferBeginOutcome
{
    Started,
    /// <summary>A transfer of this user's selection is already running (e.g. started in another tab).</summary>
    Busy,
    /// <summary>The selection is not the one the page that asked was showing.</summary>
    Changed,
    Empty
}

public sealed record TransferBegin(TransferBeginOutcome Outcome, IReadOnlyList<string> Paths);

/// <summary>
/// Keeps the selection of every user. It belongs to the user (not to a tab), so it follows them through the directories and
/// is shared by all their tabs, and it is forgotten after a while.
/// </summary>
public interface ISelectionService
{
    SelectionSnapshot Get(string userId);

    /// <summary>Selects the entries. Returns null (and changes nothing) while a transfer is running.</summary>
    SelectionSnapshot? Add(string userId, IEnumerable<SelectionEntry> entries);

    /// <summary>Unselects the paths. Returns null (and changes nothing) while a transfer is running.</summary>
    SelectionSnapshot? Remove(string userId, IEnumerable<string> paths);

    /// <summary>Unselects everything. Returns null (and changes nothing) while a transfer is running.</summary>
    SelectionSnapshot? Clear(string userId);

    /// <summary>
    /// Marks the selection as being transferred, if it is <paramref name="version"/> and nothing is running yet. While marked the
    /// selection can't be changed. Must be followed by <see cref="EndTransfer"/>.
    /// </summary>
    TransferBegin TryBeginTransfer(string userId, long version);

    /// <summary>How much of the running transfer is done (0 to 1), for the pages to show. Ignored when no transfer is running.</summary>
    void ReportProgress(string userId, double progress);

    /// <summary>Ends what <see cref="TryBeginTransfer"/> started; the selection is cleared when it was transferred successfully.</summary>
    void EndTransfer(string userId, bool clear);
}

public sealed class SelectionService(IMemoryCache cache, TimeProvider time) : ISelectionService
{
    /// <summary>How long a selection survives without being looked at or changed.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The selections that are being transferred. Nothing looks at a selection while its transfer runs, which may take longer than its
    /// <see cref="Lifetime"/>: these are held here, so that they stay busy (and nobody starts another transfer) until the transfer ends.
    /// </summary>
    private readonly ConcurrentDictionary<string, State> _transferring = new();

    public SelectionSnapshot Get(string userId)
    {
        State state = GetState(userId);
        lock (state)
            return state.Snapshot();
    }

    public SelectionSnapshot? Add(string userId, IEnumerable<SelectionEntry> entries)
        => Change(userId, state => entries.Count(entry => state.Paths.TryAdd(entry.Path, entry)));

    public SelectionSnapshot? Remove(string userId, IEnumerable<string> paths)
        => Change(userId, state => paths.Count(state.Paths.Remove));

    /// <summary>Applies the change (which returns how many entries it changed) unless a transfer is running.</summary>
    private SelectionSnapshot? Change(string userId, Func<State, int> change)
    {
        State state = GetState(userId);
        lock (state)
        {
            if (state.Busy)
                return null;

            if (change(state) > 0)
                state.Version++;

            return state.Snapshot();
        }
    }

    public SelectionSnapshot? Clear(string userId)
    {
        State state = GetState(userId);
        lock (state)
        {
            if (state.Busy)
                return null;

            if (state.Paths.Count > 0)
            {
                state.Paths.Clear();
                state.Version++;
            }

            return state.Snapshot();
        }
    }

    public TransferBegin TryBeginTransfer(string userId, long version)
    {
        State state = GetState(userId);
        lock (state)
        {
            if (state.Busy)
                return new TransferBegin(TransferBeginOutcome.Busy, []);
            if (state.Version != version)
                return new TransferBegin(TransferBeginOutcome.Changed, []);
            if (state.Paths.Count == 0)
                return new TransferBegin(TransferBeginOutcome.Empty, []);

            state.Busy = true;
            state.Progress = 0;
            state.Version++; // pages showing the selection as it was before are outdated now
            _transferring[userId] = state;
            return new TransferBegin(TransferBeginOutcome.Started, state.Snapshot().Paths);
        }
    }

    public void ReportProgress(string userId, double progress)
    {
        State state = GetState(userId);
        lock (state)
        {
            if (state.Busy)
                state.Progress = Math.Clamp(progress, 0, 1);
        }
    }

    public void EndTransfer(string userId, bool clear)
    {
        State state = GetState(userId);
        lock (state)
        {
            state.Busy = false;
            state.Progress = 0;
            if (clear)
                state.Paths.Clear();
            state.Version++;
        }

        // Back into the cache (with a fresh lifetime) before it is let go of here, so that it is found in one of the two all the time
        _ = cache.Set(CacheKey(userId), state, new MemoryCacheEntryOptions { SlidingExpiration = Lifetime });
        _ = _transferring.TryRemove(userId, out _);
    }

    private static string CacheKey(string userId) => "selection:" + userId;

    /// <summary>The user's state. Looking it up (even to read) renews its lifetime.</summary>
    private State GetState(string userId)
        => _transferring.TryGetValue(userId, out State? transferring)
            ? transferring
            : cache.GetOrCreate(CacheKey(userId), entry =>
            {
                entry.SlidingExpiration = Lifetime;
                return new State(StartVersion());
            })!;

    /// <summary>
    /// The version a new selection starts at: the time in milliseconds. A page may still show a selection that was forgotten (or one from before
    /// the server restarted), and only takes a newer version from the server; the new selection must look newer than the old one did, which had
    /// to be left alone for half an hour before it was forgotten. (Milliseconds, not finer: the scripts of the pages count in doubles.)
    /// </summary>
    private long StartVersion() => time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>All members are guarded by locking the instance.</summary>
    private sealed class State(long version)
    {
        /// <summary>The selected entries by virtual path.</summary>
        public Dictionary<string, SelectionEntry> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public long Version { get; set; } = version;
        public bool Busy { get; set; }
        public double Progress { get; set; }

        public SelectionSnapshot Snapshot()
            => new(Version, Busy, [..Paths.Keys.Order(StringComparer.OrdinalIgnoreCase)],
                Paths.Values.Count(entry => entry.IsDirectory),
                Paths.Values.All(entry => entry.CanDelete),
                Busy ? Progress : null);
    }
}
