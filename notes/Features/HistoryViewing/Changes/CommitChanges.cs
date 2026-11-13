using System.Security.Claims;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.States;

namespace notes.Features.HistoryViewing.Changes;

/// <summary>How a side of a change (before the commit, or as the commit left it) is shown.</summary>
public enum ChangeSideKind
{
    /// <summary>The entry may be seen as it was at that state.</summary>
    Shown,

    /// <summary>It wasn't there: the commit added or deleted it.</summary>
    NotThere,

    /// <summary>It was there, but can't be seen as it was.</summary>
    NotShown,
}

/// <summary>A side of a change: how it is shown, and the entry where it is.</summary>
/// <param name="Entry">The entry at that state, only where it is shown.</param>
public sealed record ChangeSide(ChangeSideKind Kind, EarlierEntry? Entry)
{
    public static readonly ChangeSide NotThere = new(ChangeSideKind.NotThere, null);
}

/// <summary>What a commit did to an entry of a listing of its changes, as it is marked there.</summary>
public enum ChangeMark
{
    Added,
    Changed,
    Deleted,
}

/// <summary>An entry of the listing of the changes of a directory.</summary>
/// <param name="Mark">What the commit did to it; null for a directory that only has changes below it.</param>
/// <param name="CanBeOpened">Whether its changes can be opened: it may be seen on at least one side.</param>
/// <param name="ExistsNow">Whether there is an entry at its path now (which one that can't be opened leads to, as without history in a listing).</param>
public sealed record ChangedEntry(string PhysicalPath, bool IsDirectory, ChangeMark? Mark, bool CanBeOpened, bool ExistsNow);

/// <summary>
/// What a commit changed, as far as a user may see it (docs/development/history-specification.md, "The changes of a commit"): the two sides
/// of a change of an entry, and what in a directory was changed.
/// </summary>
public sealed class CommitChanges(EarlierStatePermissions permissions)
{
    /// <summary>
    /// How the entry (its physical path, and as the state spells it) is shown at the state (null: before the first commit). Whether they may
    /// see its history now is checked by the caller.
    /// </summary>
    public ChangeSide SideOf(ClaimsPrincipal user, string physicalPath, string gitPath, EarlierState? state)
    {
        if (state is null)
            return ChangeSide.NotThere;

        EarlierStateVerdict verdict = permissions.CheckWhatDependsOnTheState(user, physicalPath, gitPath, state);
        if (verdict == EarlierStateVerdict.CanBeSeen)
            return new ChangeSide(ChangeSideKind.Shown, state.FindEntry(gitPath));
        // Anonymous users don't learn of what they couldn't read, as in a listing (see AccessTree.SeesUnreadableNames)
        return verdict == EarlierStateVerdict.DidNotExistThen || user.Identity?.IsAuthenticated != true
            ? ChangeSide.NotThere
            : new ChangeSide(ChangeSideKind.NotShown, null);
    }

    /// <summary>Whether the commit of the state changed the entry (or for a directory something below it) in a way the user may see.</summary>
    public bool ChangedVisibly(ClaimsPrincipal user, string gitPath, EarlierState state, EarlierState? previous)
        => new ChangeVisibility(user, permissions).VisibleChangesBelow(gitPath, state, previous).Count > 0;

    /// <summary>
    /// What in the directory (as either state spells it) the commit of the state changed in a way the user may see: each entry of it that
    /// was added, changed or deleted, or has such a change below it. Nothing else is listed.
    /// </summary>
    public IEnumerable<ChangedEntry> ListChangesOfDirectory(ClaimsPrincipal user, string gitPath, EarlierState state, EarlierState? previous)
    {
        // The visible changes below the directory, by the entry of the directory they are in (the first name below it). Both states spell
        // the directory alike but for case, so its length is the same in both.
        int start = gitPath == "" ? 0 : gitPath.Length + 1;
        IEnumerable<string> namesWithChanges = new ChangeVisibility(user, permissions).VisibleChangesBelow(gitPath, state, previous)
            .Select(file => file[start..].Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string name in namesWithChanges)
        {
            // As the commit spells it, or before it for what it deleted
            string childPath = gitPath == "" ? name : gitPath + "/" + name;
            EarlierEntry? after = state.FindEntry(childPath);
            EarlierEntry? before = previous?.FindEntry(childPath);
            EarlierEntry entry = (after ?? before)!;
            string physicalPath = state.ToPhysicalPath(entry.GitPath);

            ChangeSide beforeSide = SideOf(user, physicalPath, entry.GitPath, previous);
            ChangeSide afterSide = SideOf(user, physicalPath, entry.GitPath, state);
            yield return new ChangedEntry(physicalPath, entry.IsDirectory, MarkOf(entry.IsDirectory, beforeSide, afterSide),
                CanBeOpened: beforeSide.Kind == ChangeSideKind.Shown || afterSide.Kind == ChangeSideKind.Shown, Path.Exists(physicalPath));
        }
    }

    /// <summary>Added where it wasn't there before, deleted where it isn't after; otherwise changed (a directory: only something below it).</summary>
    private static ChangeMark? MarkOf(bool isDirectory, ChangeSide before, ChangeSide after)
    {
        if (before.Kind == ChangeSideKind.NotThere && after.Kind != ChangeSideKind.NotThere)
            return ChangeMark.Added;
        if (after.Kind == ChangeSideKind.NotThere && before.Kind != ChangeSideKind.NotThere)
            return ChangeMark.Deleted;
        return isDirectory ? null : ChangeMark.Changed;
    }
}
