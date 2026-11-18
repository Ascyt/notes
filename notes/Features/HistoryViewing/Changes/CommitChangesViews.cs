using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.Components.Toolbar;
using notes.Features.Helpers;
using notes.Features.HistoryViewing.States;
using notes.Features.HistoryViewing.Views;
using notes.Pages.Browse;
using notes.Pages.Editor;

namespace notes.Features.HistoryViewing.Changes;

/// <summary>The pages of the changes of a commit (<c>?commit=&amp;changes</c>): what it changed in a directory, or the two sides of a file.</summary>
public sealed class CommitChangesViews(Config config, NotesLocations locations, IPathService path, AccessTree tree, CommitChanges changes, EarlierStateViews views)
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
        directory.InitFromEarlierState(changes.ListChangesOfDirectory(user, gitPath, shown.State, shown.Previous).Select(changed => (
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
    private static ToolbarButton WayToState(ShowChangesOfCommit shown, string kind)
        => ToolbarButton.CommitChanges("?" + shown.Shown.QueryPart, leadsToChanges: false, shown.After.Kind switch
        {
            ChangeSideKind.Shown => null,
            ChangeSideKind.NotThere => $"This {kind} is not there in the state of this commit.",
            _ => $"Insufficient permissions to see this {kind} as it was in the state of this commit."
        });

    /// <summary>What the page shows of a side: whether it is text (and the text), or what the browser shows it as.</summary>
    /// <remarks>Text larger than <paramref name="maxTextBytes"/> is shown like binary contents: it isn't read into the page.</remarks>
    private static async Task<FileChangeSide> DescribeSideAsync(ChangeSide side, string rawBytesQuery, int maxTextBytes)
    {
        if (side.Entry?.Blob is not { } blob)
            return new FileChangeSide(side.Kind, IsBinary: false, "", MediaKind.None, "", rawBytesQuery);

        bool isBinary;
        await using (Stream contents = blob.GetContentStream())
            isBinary = blob.Size > maxTextBytes || await FileView.LooksBinaryAsync(contents);
        MediaKind media = MediaTypes.KindOf(side.Entry.Name);
        return new FileChangeSide(side.Kind, isBinary, isBinary ? "" : EarlierState.ReadText(blob), isBinary ? media : MediaKind.None,
            FileView.FormatSize(blob.Size), rawBytesQuery);
    }
}
