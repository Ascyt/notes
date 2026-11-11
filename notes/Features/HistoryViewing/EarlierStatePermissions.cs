using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.History;

namespace notes.Features.HistoryViewing;

/// <summary>Whether an entry may be seen as it was at a state, and if not, why.</summary>
public enum EarlierStateVerdict
{
    /// <summary>It may be seen as it was ("open at T" in the specification).</summary>
    CanBeSeen,

    /// <summary>The permissions don't allow it, now or then (Read or history of the entry, or Read of a directory above it).</summary>
    NotPermitted,

    /// <summary>The configuration leaves it out of the history now (gitignore). Only those who manage the configuration are told so.</summary>
    NotKeptInHistory,

    /// <summary>It was not there at that time.</summary>
    DidNotExistThen,
}

/// <summary>How an entry of a listing of an earlier state is shown.</summary>
public enum EarlierListingKind
{
    /// <summary>A link to it as it was.</summary>
    Link,

    /// <summary>Its name with the lock: it can't be read, now or then.</summary>
    Locked,

    /// <summary>Its name with a crossed-out calendar: it can be read, but its history can't be seen.</summary>
    NoHistory,

    /// <summary>Not at all: it isn't part of the history, or (for anonymous users) it can't be read.</summary>
    NotListed,
}

/// <summary>An entry of a listing of an earlier state, and how it is shown.</summary>
/// <param name="ExistsNow">Whether there is an entry at its path now, which a link to an entry without history leads to.</param>
public sealed record EarlierListingEntry(EarlierEntry Entry, string PhysicalPath, EarlierListingKind Kind, bool ExistsNow);

/// <summary>A time at which an entry can be seen as it was: after <paramref name="After"/> (the commit of a state), up to <paramref name="Until"/> (null: up to now).</summary>
public sealed record VisibleTimeRange(DateTimeOffset After, DateTimeOffset? Until);

/// <summary>
/// Applies <see cref="IEntryAccess"/> to earlier states of the notes, as <see cref="AccessTree"/> does to the notes as they are, by the rules of
/// docs/development/history-specification.md ("Who may see a state"): an entry may be seen as it was at a state if the history keeps it
/// now, it was there, the user may read it and see its history, now and then, and they may read every directory above it, now and then
/// (it is known, then as now).
/// </summary>
public sealed class EarlierStatePermissions(Config config, IEntryAccess access, HistoryService history, EarlierStateReader reader)
{
    private readonly HistoryPaths _paths = new(config.Dir);

    private string Root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.Dir));

    /// <summary>Whether the entry (physical path) may be seen as it was at the state, and if not, why.</summary>
    public EarlierStateVerdict CheckWhetherEntryCanBeSeenAsItWas(ClaimsPrincipal user, string physicalPath, EarlierState state)
    {
        // Now first: who may not see the history of the entry now learns nothing about it at that time
        if (!MaySeeHistoryOfEntryNow(user, physicalPath))
            return EarlierStateVerdict.NotPermitted;
        return CheckWhatDependsOnTheState(user, physicalPath, state.ToGitPath(physicalPath), state);
    }

    /// <summary>
    /// Whether the user may see the history of the entry at all, as far as now decides it: they may read it and every directory above it
    /// now, and have <see cref="HistoryAccess.Existing"/> on it (<see cref="HistoryAccess.ExistingAndDeleted"/> if it isn't there now).
    /// </summary>
    public bool MaySeeHistoryOfEntryNow(ClaimsPrincipal user, string physicalPath)
        => IsInsideTheNotes(physicalPath)
           && AllowsSeeingHistoryNow(access.GetAccess(user, physicalPath), Path.Exists(physicalPath))
           && DirectoriesAbove(physicalPath).All(directory => access.GetAccess(user, directory).CanRead);

    /// <summary>
    /// The directories above an entry that may be seen as it was (physical paths, the root first) that can't be seen as they were themselves,
    /// each with why. They may all be read, now and then (the entry couldn't be seen otherwise), so it is their history that is missing.
    /// </summary>
    public IEnumerable<(string PhysicalPath, EarlierStateVerdict Verdict)> DirectoriesAboveThatCantBeSeenAsTheyWere(ClaimsPrincipal user, string physicalPath, EarlierState state)
        => DirectoriesAbove(physicalPath).Reverse()
            .Select(directory => (directory, CheckWhetherEntryCanBeSeenAsItWas(user, directory, state)))
            .Where(checkedDirectory => checkedDirectory.Item2 != EarlierStateVerdict.CanBeSeen);

    /// <summary>How each entry of a directory that may be seen as it was at the state is shown in its listing.</summary>
    public IEnumerable<EarlierListingEntry> ListDirectoryAsItWas(ClaimsPrincipal user, EarlierEntry directory, EarlierState state)
    {
        // Anonymous users don't see the names of what they may not read (see AccessTree.SeesUnreadableNames)
        bool seesUnreadableNames = user.Identity?.IsAuthenticated == true;
        foreach (EarlierEntry entry in state.ListDirectory(directory))
        {
            string physicalPath = state.ToPhysicalPath(entry.GitPath);
            bool existsNow = Path.Exists(physicalPath);
            EarlierListingKind kind = HowEntryOfDirectoryIsListed(user, entry, physicalPath, existsNow, state);
            if (kind == EarlierListingKind.Locked && !seesUnreadableNames)
                kind = EarlierListingKind.NotListed;
            if (kind != EarlierListingKind.NotListed)
                yield return new EarlierListingEntry(entry, physicalPath, kind, existsNow);
        }
    }

    /// <summary>The times at which the entry may be seen as it was, the latest first.</summary>
    public IReadOnlyList<VisibleTimeRange> TimesAtWhichEntryCanBeSeen(ClaimsPrincipal user, string physicalPath)
    {
        if (!MaySeeHistoryOfEntryNow(user, physicalPath))
            return [];

        string gitPath = _paths.ToGitPath(physicalPath);
        // States in which nothing the entry's permissions depend on changed are decided alike, so each kind of state is checked once
        Dictionary<string, bool> canBeSeenByWhatItDependsOn = [];
        List<VisibleTimeRange> ranges = [];
        // The range being collected, going back in time: from the newest state in it (until it was replaced) to the oldest
        DateTimeOffset? rangeUntil = null;
        DateTimeOffset? rangeAfter = null;

        foreach ((EarlierState state, DateTimeOffset? replacedAt) in reader.EveryStateNewestFirst())
        {
            string dependsOn = state.WhatPermissionsOfPathDependOn(gitPath);
            if (!canBeSeenByWhatItDependsOn.TryGetValue(dependsOn, out bool canBeSeen))
                canBeSeenByWhatItDependsOn[dependsOn] = canBeSeen = CheckWhatDependsOnTheState(user, physicalPath, gitPath, state) == EarlierStateVerdict.CanBeSeen;

            if (canBeSeen)
            {
                // A new range starts with the newest state of it, and goes back as long as the states can be seen
                if (rangeAfter is null)
                    rangeUntil = replacedAt;
                rangeAfter = state.CommitTime;
            }
            else if (rangeAfter is { } after)
            {
                ranges.Add(new VisibleTimeRange(after, rangeUntil));
                rangeAfter = null;
            }
        }

        if (rangeAfter is { } oldest)
            ranges.Add(new VisibleTimeRange(oldest, rangeUntil));
        return ranges;
    }

    private EarlierListingKind HowEntryOfDirectoryIsListed(ClaimsPrincipal user, EarlierEntry entry, string physicalPath, bool existsNow, EarlierState state)
    {
        if (!history.IsKept(physicalPath, entry.IsDirectory))
            return EarlierListingKind.NotListed;

        // (The directories above may be read, now and then: the directory is listed as it was)
        EntryAccessInfo accessNow = access.GetAccess(user, physicalPath);
        EntryAccessInfo accessThen = access.GetAccess(user, physicalPath, state);
        // Read always comes first
        if (!accessNow.CanRead || !accessThen.CanRead)
            return EarlierListingKind.Locked;
        return AllowsSeeingHistoryNow(accessNow, existsNow) && AllowedSeeingHistoryThen(accessThen) ? EarlierListingKind.Link : EarlierListingKind.NoHistory;
    }

    /// <summary>Everything of <see cref="CheckWhetherEntryCanBeSeenAsItWas"/> but what is decided by the permissions now (<see cref="MaySeeHistoryOfEntryNow"/>).</summary>
    private EarlierStateVerdict CheckWhatDependsOnTheState(ClaimsPrincipal user, string physicalPath, string gitPath, EarlierState state)
    {
        EarlierEntry? entry = state.FindEntry(gitPath);
        // What the history leaves out now has no history, whatever it was then. (What isn't there either way is asked about as it is now.)
        if (!history.IsKept(physicalPath, entry?.IsDirectory ?? Directory.Exists(physicalPath)))
            return EarlierStateVerdict.NotKeptInHistory;
        if (entry is null)
            return EarlierStateVerdict.DidNotExistThen;

        // Read and history of the entry then, and Read of every directory above it then
        bool permittedThen = IsInsideTheNotes(physicalPath)
                             && AllowedSeeingHistoryThen(access.GetAccess(user, physicalPath, state))
                             && DirectoriesAbove(physicalPath).All(directory => access.GetAccess(user, directory, state).CanRead);
        return permittedThen ? EarlierStateVerdict.CanBeSeen : EarlierStateVerdict.NotPermitted;
    }

    /// <summary>Whether the permissions now allow seeing the history of an entry: Read, and history for an entry that is (or isn't) there now.</summary>
    private static bool AllowsSeeingHistoryNow(EntryAccessInfo now, bool existsNow)
        => now.CanRead && now.History >= (existsNow ? HistoryAccess.Existing : HistoryAccess.ExistingAndDeleted);

    /// <summary>Whether the permissions of a state allowed seeing the entry as it was then: Read, and any history.</summary>
    private static bool AllowedSeeingHistoryThen(EntryAccessInfo then)
        => then.CanRead && then.History >= HistoryAccess.Existing;

    /// <summary>Whether the path is the root of the notes or inside it. Anything else fails every check.</summary>
    private bool IsInsideTheNotes(string physicalPath)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        return string.Equals(path, Root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every directory above the path (inside the notes, see <see cref="IsInsideTheNotes"/>), up to the root of the notes; nothing for the root.</summary>
    private IEnumerable<string> DirectoriesAbove(string physicalPath)
    {
        string current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        while (!string.Equals(current, Root, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(current) is { } above)
        {
            yield return above;
            current = above;
        }
    }
}
