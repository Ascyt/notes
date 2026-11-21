using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Media;

namespace notes.Pages.Browse;

/// <summary>A media file in the grid of a listing (see <see cref="MediaEntries"/>): its picture (or icon), number and name.</summary>
public class _MediaTile : PageModel
{
    public ListedEntry Entry { get; init; } = null!;
    public string Name { get; init; } = "";
    public string VirtualPath { get; init; } = "";
    /// <summary>Image, video or audio.</summary>
    public MediaKind Kind { get; init; }

    /// <summary>Where its picture comes from: the thumbnail of an image, the video itself (of the state that is shown). Null for audio, which has none.</summary>
    public string? PictureSource { get; init; }

    /// <summary>What stands in for the picture while it loads, where it can't be loaded, and for audio.</summary>
    public string Icon => MediaEntries.IconOf(Kind);

    /// <summary>Its position in the grid, counted from 1, with leading zeroes (<see cref="DirectoryView.NumberText"/>).</summary>
    public string Number { get; init; } = "";

    /// <summary>Where it leads: the file, or for an entry without history the file as it is now. Null where it leads nowhere (locked, gone).</summary>
    public string? Href { get; init; }

    /// <summary>Alt with its number (for the first ten), where it leads anywhere.</summary>
    public string? OpenKey { get; init; }

    /// <summary>Alt+Shift with its number: Ctrl+Alt, as the other groups would have it, is AltGr on many keyboards, which types characters.</summary>
    public string? SelectKey { get; init; }

    /// <summary>Whether the listing has boxes to select entries with.</summary>
    public bool ShowsCheckbox { get; init; }

    public bool IsSelected { get; init; }
}
