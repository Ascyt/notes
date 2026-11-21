using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Components.Toolbar;
using Notes.Features.Editor;
using Notes.Features.Helpers;
using Notes.Features.HistoryViewing.States;
using Notes.Features.HistoryViewing.Views;
using Notes.Features.Media;
using Notes.Pages.Browse;

namespace Notes.Features.HistoryViewing.Changes;

/// <summary>The pages of the changes of a commit (<c>?commit=&amp;changes</c>): what it changed in a directory, or the two sides of a file.</summary>
public sealed class CommitChangesViews(Config config, EntryLocations locations, IPathService path, AccessTree tree, CommitChanges changes,
    EarlierStateViews views, ToolbarButtons buttons, HistoryTexts texts)
{
    /// <param name="addressOfNow">The address of the entry as it is now, which the sidebar leads back to.</param>
    public DirectoryView CreateDirectoryView(ClaimsPrincipal user, string virtualPath, ShowChangesOfCommit shown, string addressOfNow)
    {
        string gitPath = (shown.After.Entry ?? shown.Before.Entry)!.GitPath;
        DirectoryView directory = new(locations, path, tree)
        {
            VirtualPath = virtualPath,
            PhysicalPath = shown.PhysicalPath,
            Viewer = user,
            CanBrowse = true,
            Shown = shown.Shown,
            ShowsChanges = true,
            HistoryButton = HistoryButtonState.Usable,
            CommitSidebar = views.CommitViewSidebar(user, shown.State, shown.PhysicalPath, shown.Shown, "directory", addressOfNow),
            CommitChangesButton = WayToState(shown, "directory"),
            ParentsWithoutHistory = views.ParentsWithoutHistory(user, shown.StateShown, shown.PhysicalPath)
        };
        // What can't be opened is a directory without history, which is shown as in a listing
        directory.InitFromEarlierState(texts.CantBeChanged, changes.ListChangesOfDirectory(user, gitPath, shown.State, shown.Previous).Select(changed => (
            new ListedEntry(changed.PhysicalPath, changed.CanBeOpened ? ListedKind.Open : ListedKind.NoHistory, changed.ExistsNow, changed.Mark),
            changed.IsDirectory)));
        return directory;
    }

    /// <param name="addressOfNow">The address of the entry as it is now, which the sidebar leads back to.</param>
    public async Task<_FileChanges> CreateFileChangesAsync(ClaimsPrincipal user, string virtualPath, ShowChangesOfCommit shown, string addressOfNow)
    {
        string rawBytesQuery = "raw&" + shown.Shown.QueryPart + "&" + CommitChangesRequests.ChangesParameter;
        return new _FileChanges
        {
            VirtualPath = virtualPath,
            Before = await DescribeSideAsync(shown.Before, rawBytesQuery + "&" + CommitChangesRequests.PreviousParameter, config.Limits.MaxTextBytes),
            After = await DescribeSideAsync(shown.After, rawBytesQuery, config.Limits.MaxTextBytes),
            CommitSidebar = views.CommitViewSidebar(user, shown.State, shown.PhysicalPath, shown.Shown, "file", addressOfNow),
            CommitChangesButton = WayToState(shown, "file"),
            ParentsWithoutHistory = views.ParentsWithoutHistory(user, shown.StateShown, shown.PhysicalPath)
        };
    }

    /// <summary>The way back to the state of the commit, disabled where the entry can't be seen in it.</summary>
    private ToolbarButton WayToState(ShowChangesOfCommit shown, string kind)
        => buttons.CommitChanges("?" + shown.Shown.QueryPart, leadsToChanges: false, shown.After.Kind switch
        {
            ChangeSideKind.Shown => null,
            ChangeSideKind.NotThere => texts.NotThereInCommit(kind == "file"),
            _ => texts.CantBeSeenInCommit(kind == "file")
        });

    /// <summary>What the page shows of a side: whether it is text (and the text), or what the browser shows it as.</summary>
    /// <remarks>Text larger than <paramref name="maxTextBytes"/> is shown like binary contents: it isn't read into the page.</remarks>
    private static async Task<FileChangeSide> DescribeSideAsync(ChangeSide side, string rawBytesQuery, int maxTextBytes)
    {
        if (side.Entry?.Blob is not { } blob)
            return new FileChangeSide(side.Kind, IsBinary: false, "", MediaKind.None, "", rawBytesQuery);

        bool isBinary;
        await using (Stream contents = blob.GetContentStream())
            isBinary = blob.Size > maxTextBytes || await TextFiles.LooksBinaryAsync(contents);
        MediaKind media = MediaTypes.KindOf(side.Entry.Name);
        return new FileChangeSide(side.Kind, isBinary, isBinary ? "" : EarlierState.ReadText(blob), isBinary ? media : MediaKind.None,
            ByteSizes.Format(blob.Size), rawBytesQuery);
    }
}
