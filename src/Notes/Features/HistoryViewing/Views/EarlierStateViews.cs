using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Components.Breadcrumb;
using Notes.Features.Components.Toolbar;
using Notes.Features.Helpers;
using Notes.Features.HistoryViewing.Changes;
using Notes.Features.HistoryViewing.Commits;
using Notes.Features.HistoryViewing.Requests;
using Notes.Features.HistoryViewing.States;
using Notes.Pages.Browse;
using Notes.Pages.Editor;

namespace Notes.Features.HistoryViewing.Views;

/// <summary>
/// The pages of an entry as an earlier state has it (<c>?before=</c>): its listing or its file, with nothing in them that changes anything.
/// </summary>
public sealed class EarlierStateViews(Config config, EntryLocations locations, IPathService path, AccessTree tree,
    EarlierStatePermissions permissions, EarlierStateCommits commits,
    CommitChanges changes, ToolbarButtons buttons, HistoryTexts texts)
{
    /// <summary>Why nothing can be changed in an earlier state, which the controls that would change something say.</summary>

    /// <param name="addressOfNow">The address of the entry as it is now, which the view of a commit leads back to.</param>
    public DirectoryView CreateDirectoryView(ClaimsPrincipal user, string virtualPath, ShowEntryAsItWas shown, string addressOfNow)
    {
        DirectoryView directory = new(locations, path, tree)
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
        directory.InitFromEarlierState(texts.CantBeChanged, permissions.ListDirectoryAsItWas(user, shown.Entry, shown.State).Select(listed => (
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
    public async Task<FileView> CreateFileViewAsync(ClaimsPrincipal user, string virtualPath, ShowEntryAsItWas shown, string addressOfNow,
        bool editRequested, string siteHost)
    {
        LibGit2Sharp.Blob blob = shown.Entry.Blob!;
        FileView file = new()
        {
            VirtualPath = virtualPath,
            PhysicalPath = shown.PhysicalPath,
            MaxTextBytes = config.Limits.MaxTextBytes,
            Earlier = new EarlierFile(shown.Shown, blob.Size, () => blob.GetContentStream()),
            MediaNeighbours = MediaEntries.IsMedia(shown.Entry.Name) ? MediaNeighboursAsTheyWere(user, shown) : null,
            HistoryButton = HistoryButtonState.Usable,
            CommitSidebar = CommitViewSidebar(user, shown.State, shown.PhysicalPath, shown.Shown, "file", addressOfNow),
            CommitChangesButton = WayToChanges(user, shown, "file"),
            ParentsWithoutHistory = ParentsWithoutHistory(user, shown.State, shown.PhysicalPath),
            EditRequested = editRequested,
            SiteHost = siteHost,
            DeleteDeniedReason = texts.CantBeChanged,
            RenameDeniedReason = texts.CantBeChanged
        };
        await file.InitAsync();
        return file;
    }

    /// <summary>
    /// The media next to a file in its directory as it was, among those that could be opened then (as the grid of the directory in that state
    /// has them). The links lead to them as they are now; the card of the state adds the state to them.
    /// </summary>
    private MediaNeighbours MediaNeighboursAsTheyWere(ClaimsPrincipal user, ShowEntryAsItWas shown)
    {
        string gitPath = shown.Entry.GitPath;
        string parentGitPath = gitPath.Contains('/') ? gitPath[..gitPath.LastIndexOf('/')] : "";
        if (shown.State.FindEntry(parentGitPath) is not { IsDirectory: true } parent)
            return MediaNeighbours.None;

        IEnumerable<(string, string)> openable = permissions.ListDirectoryAsItWas(user, parent, shown.State)
            .Where(listed => listed.Kind == EarlierListingKind.Link && !listed.Entry.IsDirectory && MediaEntries.IsMedia(listed.Entry.Name))
            .Select(listed => (listed.Entry.Name, VirtualPaths.ToAddress(path.ToVirtualPath(listed.PhysicalPath))));
        return MediaEntries.NeighboursOf(shown.Entry.Name, openable);
    }

    /// <summary>
    /// In the view of a commit, the way to what it changed of the entry, disabled where it changed nothing of it that may be seen; otherwise
    /// null.
    /// </summary>
    private ToolbarButton? WayToChanges(ClaimsPrincipal user, ShowEntryAsItWas shown, string kind)
    {
        if (!shown.Shown.IsCommit)
            return null;

        using EarlierState? previous = shown.State.OpenPrevious();
        bool changed = changes.ChangedVisibly(user, shown.Entry.GitPath, shown.State, previous);
        return buttons.CommitChanges("?" + shown.Shown.QueryPart + "&" + CommitChangesRequests.ChangesParameter, leadsToChanges: true,
            changed ? null : texts.NothingChangedByCommit(kind == "file"));
    }

    /// <summary>
    /// In the view of a commit (and its changes), the sidebar of the list of changes, which is shown there from the start: with its first page
    /// from that commit back, without asking for it. Elsewhere null, as it is asked for when it is first shown.
    /// </summary>
    public _CommitSidebar? CommitViewSidebar(ClaimsPrincipal user, EarlierState state, string physicalPath, EarlierStateParameter shown,
        string kind, string addressOfNow)
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
    public _EarlierStateCard CreateCard(ClaimsPrincipal user, EarlierState state, EarlierStateParameter shown, string backToNowHref,
        bool showsChanges = false)
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

    /// <summary>
    /// The directories above the entry that can't be seen as they were at the state, by virtual path, with the reason the breadcrumb gives.
    /// </summary>
    public Dictionary<string, ParentWithoutHistory> ParentsWithoutHistory(ClaimsPrincipal user, EarlierState state, string physicalPath)
    {
        // The breadcrumb is made from the address, which may differ in case from the names on disk
        Dictionary<string, ParentWithoutHistory> parents = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string parentPath, EarlierStateVerdict verdict) in permissions.DirectoriesAboveThatCantBeSeenAsTheyWere(user, physicalPath, state))
        {
            string virtualPath = path.ToVirtualPath(parentPath);
            // Only those who manage the configuration may know what it leaves out (as the notice of the page it leads to says)
            string reason = verdict == EarlierStateVerdict.NotKeptInHistory && tree.CanManageConfiguration(user)
                ? texts.DirectoryLeftOut
                : texts.DirectoryCantBeSeen;
            bool existsNow = Directory.Exists(parentPath);
            parents[virtualPath == "." ? "" : virtualPath] = new ParentWithoutHistory(existsNow ? reason : reason + texts.GoneNow,
                existsNow);
        }

        return parents;
    }
}
