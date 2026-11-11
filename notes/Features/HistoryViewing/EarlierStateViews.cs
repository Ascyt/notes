using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Components.Breadcrumb;
using notes.Features.Helpers;
using notes.Pages.Browse;
using notes.Pages.Editor;

namespace notes.Features.HistoryViewing;

/// <summary>The pages of an entry as an earlier state has it (<c>?before=</c>): its listing or its file, with nothing in them that changes anything.</summary>
public sealed class EarlierStateViews(Config config, IPathService path, AccessTree tree, EarlierStatePermissions permissions)
{
    /// <summary>Why nothing can be changed in an earlier state, which the controls that would change something say.</summary>
    public const string CantBeChangedReason = "An earlier state can't be changed.";

    public DirectoryView CreateDirectoryView(ClaimsPrincipal user, string virtualPath, ShowEntryAsItWas shown)
    {
        DirectoryView directory = new(config, path, tree)
        {
            VirtualPath = virtualPath,
            PhysicalPath = shown.PhysicalPath,
            Viewer = user,
            CanBrowse = true,
            Before = shown.Before,
            HistoryButton = HistoryButtonState.Usable,
            ParentsWithoutHistory = ParentsWithoutHistory(user, shown)
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

    public async Task<FileView> CreateFileViewAsync(ClaimsPrincipal user, string virtualPath, ShowEntryAsItWas shown, bool editRequested, string siteHost)
    {
        LibGit2Sharp.Blob blob = shown.Entry.Blob!;
        FileView file = new()
        {
            VirtualPath = virtualPath,
            PhysicalPath = shown.PhysicalPath,
            Earlier = new EarlierFile(shown.Before, blob.Size, () => blob.GetContentStream()),
            HistoryButton = HistoryButtonState.Usable,
            ParentsWithoutHistory = ParentsWithoutHistory(user, shown),
            EditRequested = editRequested,
            SiteHost = siteHost,
            DeleteDeniedReason = CantBeChangedReason,
            RenameDeniedReason = CantBeChangedReason
        };
        await file.InitAsync();
        return file;
    }

    /// <summary>The directories above the entry that can't be seen as they were, by virtual path, with the reason the breadcrumb gives.</summary>
    private Dictionary<string, ParentWithoutHistory> ParentsWithoutHistory(ClaimsPrincipal user, ShowEntryAsItWas shown)
    {
        // The breadcrumb is made from the address, which may differ in case from the names on disk
        Dictionary<string, ParentWithoutHistory> parents = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string physicalPath, EarlierStateVerdict verdict) in permissions.DirectoriesAboveThatCantBeSeenAsTheyWere(user, shown.PhysicalPath, shown.State))
        {
            string virtualPath = path.ToVirtualPath(config.Dir, physicalPath);
            // Only those who manage the configuration may know what it leaves out (as the notice of the page it leads to says)
            string reason = verdict == EarlierStateVerdict.NotKeptInHistory && tree.CanManageConfiguration(user)
                ? "The configuration leaves this directory out of the history."
                : "Insufficient permissions to see this directory as it was.";
            bool existsNow = Directory.Exists(physicalPath);
            parents[virtualPath == "." ? "" : virtualPath] = new ParentWithoutHistory(existsNow ? reason : reason + " It is not there any more.", existsNow);
        }

        return parents;
    }
}
