using System.Security.Claims;
using notes.Features.HistoryViewing.States;

namespace notes.Features.HistoryViewing.Commits;

/// <summary>
/// Whether the user may see a change to a file (docs/development/history-specification.md, "The list of changes"): they may see its history
/// now, and it may be seen as it was at the commit (what it became) or in the state before (what it was, for what was deleted). What is no
/// entry of the notes never can. Made for one user and one request, as it keeps what it found out about now.
/// </summary>
public sealed class ChangeVisibility(ClaimsPrincipal user, EarlierStatePermissions permissions)
{
    /// <summary>Now is the same for every commit, so it is asked once per file.</summary>
    private readonly Dictionary<string, bool> _maySeeHistoryNow = new(StringComparer.OrdinalIgnoreCase);

    public bool CanChangeBeSeen(string gitPath, EarlierState state, EarlierState? previous)
    {
        string physicalPath = state.ToPhysicalPath(gitPath);
        if (!_maySeeHistoryNow.TryGetValue(physicalPath, out bool maySeeNow))
            _maySeeHistoryNow[physicalPath] = maySeeNow = permissions.MaySeeHistoryOfEntryNow(user, physicalPath);

        return maySeeNow
               && (permissions.CheckWhatDependsOnTheState(user, physicalPath, gitPath, state) == EarlierStateVerdict.CanBeSeen
                   || (previous is not null && permissions.CheckWhatDependsOnTheState(user, physicalPath, gitPath, previous) == EarlierStateVerdict.CanBeSeen));
    }

    /// <summary>The files the commit of the state changed at the path or below it (git paths) that the user may see the change of.</summary>
    public IReadOnlyList<string> VisibleChangesBelow(string gitPath, EarlierState state, EarlierState? previous)
        => [.. state.FilesChangedSince(previous, gitPath).Where(file => CanChangeBeSeen(file, state, previous))];
}
