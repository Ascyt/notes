using Microsoft.AspNetCore.Mvc.RazorPages;
using Notes.Features.Helpers;
using Notes.Features.HistoryViewing.Changes;
using Notes.Features.Media;

namespace Notes.Pages.Browse.MediaGrid;

/// <summary>
/// The images, videos and audio of a listing (<see cref="MediaEntries"/>), as pictures in a grid, each with its number and name below it.
/// Only there if there are any: new ones are added through the files, or uploaded.
/// </summary>
public class _MediaGroup : PageModel
{
    /// <summary>The styles of the grid, by their path in the library (see <see cref="Features.Components.Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Pages/Browse/MediaGrid/_MediaGroup.css";

    /// <summary>The listing the group is part of: its entries, and what may be done with them.</summary>
    public DirectoryView Listing { get; init; } = null!;

    /// <summary>The tile of a media file of the grid.</summary>
    public _MediaTile Tile(ListedEntry entry, int index)
    {
        string virtualPath = Listing.ToVirtualPath(entry.Path);
        string name = Listing.NameOf(entry.Path);
        MediaKind kind = MediaTypes.KindOf(name);
        // Of the state that is shown, and in the changes of a commit of the side that is there: before it, for what it deleted
        string state = (Listing.Shown is { } shown ? "&" + shown.QueryPart : "")
            + (Listing.ShowsChanges
                ? "&" + CommitChangesRequests.ChangesParameter
                    + (entry.Mark == ChangeMark.Deleted ? "&" + CommitChangesRequests.PreviousParameter : "")
                : "");
        bool leadsToNow = entry.Kind == ListedKind.NoHistory && entry.ExistsNow;
        return new _MediaTile
        {
            Entry = entry,
            Name = name,
            VirtualPath = virtualPath,
            Kind = kind,
            // A video shows its first frame, which the browser takes from the video itself (a little after the start, which is often black)
            PictureSource = kind switch
            {
                MediaKind.Image => VirtualPaths.ToAddress(virtualPath) + "?thumbnail" + state,
                MediaKind.Video => VirtualPaths.ToAddress(virtualPath) + "?raw" + state + "#t=0.5",
                _ => null
            },
            Number = DirectoryView.NumberText(index, Listing.Media.Length),
            OpenKey = entry.CanOpen || leadsToNow ? DirectoryView.NumberKey(index, "!") : null,
            SelectKey = DirectoryView.NumberKey(index, "!+"),
            Href = entry.CanOpen || leadsToNow ? VirtualPaths.ToAddress(virtualPath) : null,
            ShowsCheckbox = Listing.CanSelect,
            IsSelected = Listing.IsSelected(virtualPath)
        };
    }
}
