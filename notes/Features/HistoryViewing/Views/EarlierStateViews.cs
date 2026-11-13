using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Components.Breadcrumb;
using notes.Features.Components.Toolbar;
using notes.Features.Helpers;
using notes.Features.HistoryViewing.Changes;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Requests;
using notes.Features.HistoryViewing.States;
using notes.Pages.Browse;
using notes.Pages.Editor;

namespace notes.Features.HistoryViewing.Views;

/// <summary>The pages of an entry as an earlier state has it (<c>?before=</c>): its listing or its file, with nothing in them that changes anything.</summary>
public sealed class EarlierStateViews(Config config, IPathService path, AccessTree tree, EarlierStatePermissions permissions, EarlierStateCommits commits,
    CommitChanges changes)
{
    /// <summary>Why nothing can be changed in an earlier state, which the controls that would change something say.</summary>
    public const string CantBeChangedReason = "An earlier state can't be changed.";

    /// <param name="addressOfNow">The address of the entry as it is now, which the view of a commit leads back to.</param>
    public DirectoryView CreateDirectoryView(ClaimsPrincipal user, string virtualPath, ShowEntryAsItWas shown, string addressOfNow)
    {
        DirectoryView directory = new(config, path, tree)
        {
            VirtualPath = virtualPath,
            PhysicalPath = shown.PhysicalPath,
            Viewer = user,
            CanBrowse = true,
            Shown = shown.Shown,
            HistoryButton = HistoryButtonState.Usable,
            CommitSidebar = CommitViewSidebar(user, shown.State, shown.PhysicalPath, shown.Shown, "directory", addressOfNow),
            CommitChangesButton = WayToChanges(user, shown, "directory"),
            ParentsWithoutHistory = ParentsWithoutHistory(user, shown.State, shown.PhysicalPath)
        };
        directory.InitFromEarlierState(permissions.ListDirectoryAsItWas(user, shown.Entry, shown.State).Select(listed => (
            new ListedEntry(listed.PhysicalPath, listed.Kind switch
            {
                EarlierListingKind.Link => ListedKind.Open,
                EarlierListingKind.Locked => ListedKind.Locked,
                _ => ListedKind.NoHistory
            }, listed.ExistsNow),
            listed.Entry.IsDirectory)));
        return directory;
    }

    /// <param name="addressOfNow">The address of the entry as it is now, which the view of a commit leads back to.</param>
    public async Task<FileView> CreateFileViewAsync(ClaimsPrincipal user, string virtualPath, ShowEntryAsItWas shown, string addressOfNow, bool editRequested, string siteHost)
    {
        LibGit2Sharp.Blob blob = shown.Entry.Blob!;
        FileView file = new()
        {
            VirtualPath = virtualPath,
            PhysicalPath = shown.PhysicalPath,
            Earlier = new EarlierFile(shown.Shown, blob.Size, () => blob.GetContentStream()),
            HistoryButton = HistoryButtonState.Usable,
            CommitSidebar = CommitViewSidebar(user, shown.State, shown.PhysicalPath, shown.Shown, "file", addressOfNow),
            CommitChangesButton = WayToChanges(user, shown, "file"),
            ParentsWithoutHistory = ParentsWithoutHistory(user, shown.State, shown.PhysicalPath),
            EditRequested = editRequested,
            SiteHost = siteHost,
            DeleteDeniedReason = CantBeChangedReason,
            RenameDeniedReason = CantBeChangedReason
        };
        await file.InitAsync();
        return file;
    }

    /// <summary>In the view of a commit, the way to what it changed of the entry, disabled where it changed nothing of it that may be seen; otherwise null.</summary>
    private ToolbarButton? WayToChanges(ClaimsPrincipal user, ShowEntryAsItWas shown, string kind)
    {
        if (!shown.Shown.IsCommit)
            return null;

        using EarlierState? previous = shown.State.OpenPrevious();
        bool changed = changes.ChangedVisibly(user, shown.Entry.GitPath, shown.State, previous);
        return ToolbarButton.CommitChanges("?" + shown.Shown.QueryPart + "&" + CommitChangesRequests.ChangesParameter, leadsToChanges: true,
            changed ? null : $"Nothing of this {kind} was changed by this commit.");
    }

    /// <summary>
    /// In the view of a commit (and its changes), the sidebar of the list of changes, which is shown there from the start: with its first page
    /// from that commit back, without asking for it. Elsewhere null, as it is asked for when it is first shown.
    /// </summary>
    public _CommitSidebar? CommitViewSidebar(ClaimsPrincipal user, EarlierState state, string physicalPath, EarlierStateParameter shown, string kind, string addressOfNow)
        => shown.IsCommit
            ? new _CommitSidebar
            {
                Kind = kind,
                AddressOfNow = addressOfNow,
                Contents = _CommitList.Of(commits.PageOfChanges(user, physicalPath, state.CommitId, page: 1), state.CommitId)
            }
            : null;

    /// <summary>
    /// The card that says which state is shown: the time asked for, or the commit (with what it was, if the user may know of it), or its
    /// changes (<paramref name="showsChanges"/>).
    /// </summary>
    public _EarlierStateCard CreateCard(ClaimsPrincipal user, EarlierState state, EarlierStateParameter shown, string backToNowHref, bool showsChanges = false)
    {
        if (shown.IsCommit)
            return new _EarlierStateCard
            {
                Shown = shown,
                ShowsChanges = showsChanges,
                Time = state.CommitTime,
                Commit = commits.CommitTheUserMayKnowOf(user, state),
                BackToNowHref = backToNowHref
            };

        _ = EarlierStateTime.TryParse(shown.Value, out DateTimeOffset before);
        return new _EarlierStateCard { Shown = shown, Time = before, BackToNowHref = backToNowHref };
    }

    /// <summary>The directories above the entry that can't be seen as they were at the state, by virtual path, with the reason the breadcrumb gives.</summary>
    public Dictionary<string, ParentWithoutHistory> ParentsWithoutHistory(ClaimsPrincipal user, EarlierState state, string physicalPath)
    {
        // The breadcrumb is made from the address, which may differ in case from the names on disk
        Dictionary<string, ParentWithoutHistory> parents = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string parentPath, EarlierStateVerdict verdict) in permissions.DirectoriesAboveThatCantBeSeenAsTheyWere(user, physicalPath, state))
        {
            string virtualPath = path.ToVirtualPath(config.Dir, parentPath);
            // Only those who manage the configuration may know what it leaves out (as the notice of the page it leads to says)
            string reason = verdict == EarlierStateVerdict.NotKeptInHistory && tree.CanManageConfiguration(user)
                ? "The configuration leaves this directory out of the history."
                : "Insufficient permissions to see this directory as it was.";
            bool existsNow = Directory.Exists(parentPath);
            parents[virtualPath == "." ? "" : virtualPath] = new ParentWithoutHistory(existsNow ? reason : reason + " It is not there any more.", existsNow);
        }

        return parents;
    }
}
