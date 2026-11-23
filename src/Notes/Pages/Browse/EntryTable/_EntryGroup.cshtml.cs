using Microsoft.AspNetCore.Mvc.RazorPages;
using Notes.Features.Helpers;
using Notes.Features.HistoryViewing.Changes;

namespace Notes.Pages.Browse.EntryTable;

/// <summary>Which of the two tables of a listing a group is.</summary>
public enum EntryGroupKind
{
    Directories,

    /// <summary>The files without the media, which have a grid of their own (<see cref="MediaGrid._MediaGroup"/>).</summary>
    Files
}

/// <summary>
/// One of the two tables of a listing: its directories or its files, each a row with its number, icon and name, and below them the button
/// that adds one. An empty group is only its button, which then says what it adds.
/// </summary>
public class _EntryGroup : PageModel
{
    /// <summary>The styles of the tables, by their path in the library (see <see cref="Features.Components.Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Pages/Browse/EntryTable/_EntryGroup.css";

    public EntryGroupKind Kind { get; init; }

    /// <summary>The listing the group is part of: its entries, and what may be done with them.</summary>
    public DirectoryView Listing { get; init; } = null!;

    /// <summary>
    /// Whether it is the last part of the listing, with no margin below it: the page's own padding is all there is before the footer.
    /// </summary>
    public bool IsLast { get; init; }

    public bool IsDirectories => Kind == EntryGroupKind.Directories;

    public ListedEntry[] Entries => IsDirectories ? Listing.Directories : Listing.Files;

    /// <summary>The name of the group, in its id and for the selection script (<c>data-group</c>).</summary>
    public string Group => IsDirectories ? "directories" : "files";

    public string IconOf(ListedEntry entry) => IsDirectories ? EntryIcons.Directory : EntryIcons.OfFile(Listing.NameOf(entry.Path));

    // The shortcuts: the number opens, Ctrl and the number selects, "a" selects all; with Shift for the files (Alt is for the media)
    private string Modifiers => IsDirectories ? "" : "+";

    public string SelectAllKey => Modifiers + "a";

    public string? OpenKey(int index) => DirectoryView.NumberKey(index, Modifiers);

    public string? SelectKey(int index) => DirectoryView.NumberKey(index, "^" + Modifiers);

    public string Number(int index) => DirectoryView.NumberText(index, Entries.Length);

    /// <summary>The look of the link of an entry: what a commit deleted has its name struck through, as it is gone after it.</summary>
    public static string LinkDecoration(ListedEntry entry)
        => entry.Mark == ChangeMark.Deleted ? "text-decoration-line-through" : "text-decoration-none";

    /// <summary>The row of an entry that can't be opened (see <see cref="ListedEntry.CanOpen"/>).</summary>
    public _UnopenableEntry UnopenableRow(ListedEntry entry, int index)
    {
        // One without history leads to the entry as it is now, whose page says why; one that is gone now leads nowhere, nor does a locked one.
        // One that a password locks leads to where it is asked for.
        string? href = (entry.Kind == ListedKind.NoHistory && entry.ExistsNow) || entry.Kind == ListedKind.PasswordLocked
            ? VirtualPaths.ToAddress(Listing.ToVirtualPath(entry.Path))
            : null;
        return new _UnopenableEntry
        {
            Name = Listing.NameOf(entry.Path),
            IsDirectory = IsDirectories,
            Icon = IconOf(entry),
            Kind = entry.Kind,
            Number = Number(index),
            Href = href,
            Key = href is null ? null : OpenKey(index),
            ShowsCheckbox = Listing.CanSelect
        };
    }
}
