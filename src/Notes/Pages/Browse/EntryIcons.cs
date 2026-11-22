using Notes.Features.Media;

namespace Notes.Pages.Browse;

/// <summary>The icons of the entries (Font Awesome classes), the same in the listing and the breadcrumb.</summary>
public static class EntryIcons
{
    public const string Directory = "fa-solid fa-folder";

    /// <summary>The icon of a file of that name: for media what kind it is (<see cref="MediaEntries.IconOf"/>), otherwise a plain file.</summary>
    public static string OfFile(string name)
        => MediaEntries.IconOf(MediaTypes.KindOf(name));
}
