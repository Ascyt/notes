using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.History;
using notes.Features.HistoryViewing.States;

namespace notes.Features.HistoryViewing.Commits;

/// <summary>A commit as the list of changes and the card of its state show it.</summary>
/// <param name="Id">Its full hash, for <c>?commit=</c>.</param>
/// <param name="Description">Why it was made, as text (<see cref="CommitDescriptions"/>).</param>
/// <param name="Author">The name of the user it is by, or null for what no user made in the app.</param>
public sealed record CommitSummary(string Id, DateTimeOffset Time, string Description, string? Author);

/// <summary>A part of the list of changes of an entry: a page of it, or the newer commits to add on top of it.</summary>
/// <param name="Commits">The newest first.</param>
/// <param name="Page">Which page (from 1, the newest) it is.</param>
/// <param name="HasOlder">Whether there is a next, older page.</param>
/// <param name="NewerAfter">The id of the commit after which the list has newer ones (to be asked for), or null if there are none.</param>
public sealed record CommitList(IReadOnlyList<CommitSummary> Commits, int Page, bool HasOlder, string? NewerAfter);

/// <summary>
/// The commits a user may know of (docs/development/history-specification.md, "The list of changes"): those that changed a file they may see as it
/// was, at the commit or in the state before it. A commit that only changed what they may not see (or only configuration files) is not one
/// they learn of, nor who made it.
/// </summary>
public sealed class EarlierStateCommits(Config config, EarlierStatePermissions permissions, EarlierStateReader reader, AccessTree tree)
{
    public const int PageSize = 25;

    /// <summary>
    /// The commits that changed the entry (physical path), or for a directory what is in it, in a way the user may see, and at which the entry
    /// itself may be seen as it was (so that each can be opened): the newest first, from the commit with the id (or the newest) back. The
    /// <paramref name="page"/>-th (from 1) <see cref="PageSize"/> of them; on the first, whether there are newer ones than that commit.
    /// Whether the user may see the history of the entry now is checked by the caller.
    /// </summary>
    public CommitList PageOfChanges(ClaimsPrincipal user, string physicalPath, string? fromCommitId, int page)
    {
        ChangeVisibility visibility = new(user, permissions);
        string gitPath = new HistoryPaths(config.Dir).ToGitPath(physicalPath);
        bool managesConfiguration = tree.CanManageConfiguration(user);
        int toSkip = (page - 1) * PageSize;
        List<CommitSummary> found = [];
        bool hasOlder = false;

        foreach ((EarlierState state, EarlierState? previous) in reader.EveryStateWithPreviousNewestFirst(fromCommitId))
        {
            if (!IsListed(user, physicalPath, gitPath, state, previous, visibility) || toSkip-- > 0)
                continue;
            // One more than the page holds says that there are older ones
            if (found.Count == PageSize)
            {
                hasOlder = true;
                break;
            }
            found.Add(Summarize(state, managesConfiguration));
        }

        // Newer ones are added on top of the first page only
        bool hasNewer = page == 1 && fromCommitId is not null && HasCommitsNewerThan(user, physicalPath, gitPath, fromCommitId, visibility);
        return new CommitList(found, page, hasOlder, hasNewer ? fromCommitId : null);
    }

    /// <summary>
    /// The <see cref="PageSize"/> commits of the list (as <see cref="PageOfChanges"/>) that come right after the commit with the id, the newest
    /// first, and whether there are newer ones still. Nothing if the history has no such commit.
    /// </summary>
    public CommitList NewerChanges(ClaimsPrincipal user, string physicalPath, string commitId)
    {
        ChangeVisibility visibility = new(user, permissions);
        string gitPath = new HistoryPaths(config.Dir).ToGitPath(physicalPath);
        bool managesConfiguration = tree.CanManageConfiguration(user);
        List<CommitSummary> newer = [];

        // From the newest back to the commit: the history is walked from its newest end only
        foreach ((EarlierState state, EarlierState? previous) in reader.EveryStateWithPreviousNewestFirst(null))
        {
            if (state.CommitId == commitId)
            {
                List<CommitSummary> closest = [.. newer.TakeLast(PageSize)];
                return new CommitList(closest, Page: 1, HasOlder: false, newer.Count > PageSize ? closest[0].Id : null);
            }
            if (IsListed(user, physicalPath, gitPath, state, previous, visibility))
                newer.Add(Summarize(state, managesConfiguration));
        }

        return new CommitList([], Page: 1, HasOlder: false, NewerAfter: null);
    }

    /// <summary>Whether the list has any commit newer than the one with the id: it stops at the first.</summary>
    private bool HasCommitsNewerThan(ClaimsPrincipal user, string physicalPath, string gitPath, string commitId, ChangeVisibility visibility)
    {
        foreach ((EarlierState state, EarlierState? previous) in reader.EveryStateWithPreviousNewestFirst(null))
        {
            if (state.CommitId == commitId)
                return false;
            if (IsListed(user, physicalPath, gitPath, state, previous, visibility))
                return true;
        }

        return false;
    }

    /// <summary>Whether the commit of the state is in the list of changes of the entry: it changed the entry visibly, and the entry may be seen at it.</summary>
    private bool IsListed(ClaimsPrincipal user, string physicalPath, string gitPath, EarlierState state, EarlierState? previous, ChangeVisibility visibility)
    {
        // Cheapest first: most commits didn't touch the entry at all
        IReadOnlyList<string> changed = state.FilesChangedSince(previous, gitPath);
        return changed.Count > 0
               && permissions.CheckWhatDependsOnTheState(user, physicalPath, gitPath, state) == EarlierStateVerdict.CanBeSeen
               && changed.Any(file => visibility.CanChangeBeSeen(file, state, previous));
    }

    /// <summary>The commit of the state, if the user may know of it: it changed anything of the notes they may see as it was. Otherwise null.</summary>
    public CommitSummary? CommitTheUserMayKnowOf(ClaimsPrincipal user, EarlierState state)
    {
        ChangeVisibility visibility = new(user, permissions);
        using EarlierState? previous = state.OpenPrevious();
        return state.FilesChangedSince(previous, "").Any(file => visibility.CanChangeBeSeen(file, state, previous))
            ? Summarize(state, tree.CanManageConfiguration(user))
            : null;
    }

    private static CommitSummary Summarize(EarlierState state, bool managesConfiguration)
        => new(state.CommitId, state.CommitTime, CommitDescriptions.Describe(state.CommitMessage, managesConfiguration),
            state.AuthorName == HistoryAuthor.App.Name ? null : state.AuthorName);
}
