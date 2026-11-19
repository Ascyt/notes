using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.Helpers;
using notes.Pages.Editor;

namespace notes.Pages.Browse;

/// <summary>The media next to one in its directory: the addresses of the one before and after it in the grid, null at either end.</summary>
public sealed record MediaNeighbours(string? Previous, string? Next)
{
    public static readonly MediaNeighbours None = new(null, null);
}

/// <summary>
/// The files a listing shows as tiles (images, videos and audio) in a grid of their own, instead of among the other files, and the way from one to
/// the next. They are in the same order as the files (by name, the newest of dated names first).
/// </summary>
public static class MediaEntries
{
    public static bool IsMedia(string name)
        => MediaTypes.KindOf(name) is MediaKind.Image or MediaKind.Video or MediaKind.Audio;

    /// <summary>The icon of a file of that kind (Font Awesome classes): the breadcrumb's, and a tile's while its picture loads or where it has none.</summary>
    public static string IconOf(MediaKind kind) => kind switch
    {
        MediaKind.Image => "fa-regular fa-file-image",
        MediaKind.Video => "fa-regular fa-file-video",
        MediaKind.Audio => "fa-regular fa-file-audio",
        _ => "fa-regular fa-file"
    };

    /// <summary>The order of a listing: by name, descending, so that names that start with a date have the newest first.</summary>
    public static IOrderedEnumerable<T> InListingOrder<T>(IEnumerable<T> entries, Func<T, string> nameOf)
        => entries.OrderByDescending(nameOf, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The neighbours of the file named <paramref name="name"/> among the media of its directory that can be opened (<paramref name="openableMedia"/>,
    /// as name and address, in any order). A file that isn't among them has none.
    /// </summary>
    public static MediaNeighbours NeighboursOf(string name, IEnumerable<(string Name, string Address)> openableMedia)
    {
        (string Name, string Address)[] ordered = [.. InListingOrder(openableMedia, media => media.Name)];
        int index = Array.FindIndex(ordered, media => string.Equals(media.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return MediaNeighbours.None;
        return new MediaNeighbours(index > 0 ? ordered[index - 1].Address : null, index < ordered.Length - 1 ? ordered[index + 1].Address : null);
    }

    /// <summary>The neighbours of a media file as its directory is now, among those the user may open (as the grid of the directory has them).</summary>
    public static MediaNeighbours NeighboursNow(ClaimsPrincipal user, string physicalPath, NotesLocations locations, AccessTree tree, IPathService path)
    {
        string name = locations.NameOf(physicalPath);
        if (!IsMedia(name) || Path.GetDirectoryName(physicalPath) is not { } directory)
            return MediaNeighbours.None;

        IEnumerable<(string, string)> openable = locations.ListDirectory(new DirectoryInfo(directory), ListOptions)
            .Where(child => child.Entry is FileInfo && IsMedia(child.Name) && tree.GetAccess(user, child.Entry.FullName, isDirectory: false).CanRead)
            .Select(child => (child.Name, VirtualPaths.ToAddress(path.ToVirtualPath(child.Entry.FullName))));
        return NeighboursOf(name, openable);
    }

    /// <summary>Hidden and system entries are entries like any other (as in the listing).</summary>
    private static readonly EnumerationOptions ListOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };
}
