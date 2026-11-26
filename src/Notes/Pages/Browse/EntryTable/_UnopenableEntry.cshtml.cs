using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Pages.Browse.EntryTable;

/// <summary>
/// A row of the listing for an entry that can't be opened: one the user may not read (with a lock), or, in an earlier state, one whose
/// history can't be seen (with a crossed-out calendar). Only its name is shown. Only one a password locks can be selected (the card then
/// offers to unlock it).
/// </summary>
public class _UnopenableEntry : PageModel
{
    public string Name { get; init; } = "";
    public bool IsDirectory { get; init; }

    /// <summary>The icon of the entry (<see cref="EntryIcons"/>), as the other rows have it.</summary>
    public string Icon { get; init; } = "";

    /// <summary>
    /// Why it can't be opened: <see cref="ListedKind.Locked"/>, <see cref="ListedKind.PasswordLocked"/> or <see cref="ListedKind.NoHistory"/>.
    /// </summary>
    public ListedKind Kind { get; init; }

    /// <summary>Its position in the listing, counted from 1, with leading zeroes (<see cref="DirectoryView.NumberText"/>).</summary>
    public string Number { get; init; } = "";

    /// <summary>
    /// Where it leads, if anywhere: an entry without history leads to the entry as it is now, whose page says why it can't be seen as it was
    /// (unless it is gone now). A locked one leads nowhere; one a password locks to where the password is asked for.
    /// </summary>
    public string? Href { get; init; }

    /// <summary>The shortcut of the link, if it is one.</summary>
    public string? Key { get; init; }

    /// <summary>Whether the other rows have a box to select them, which this one then has too (disabled, unless it <see cref="IsSelectable"/>).</summary>
    public bool ShowsCheckbox { get; init; }

    /// <summary>Its virtual path, and the group it is in (for the selection script), which a row that can be selected has.</summary>
    public string VirtualPath { get; init; } = "";

    public string Group { get; init; } = "";

    public bool IsSelected { get; init; }

    /// <summary>The shortcut that selects it, as the other rows have.</summary>
    public string? SelectKey { get; init; }

    /// <summary>Whether it can be selected: one a password locks, where the others can be.</summary>
    public bool IsSelectable => ShowsCheckbox && Kind == ListedKind.PasswordLocked;

    /// <summary>How it is kept, if it is an archived folder a password locks (its zipper goes after the lock); otherwise null.</summary>
    public Features.ZippedFolders.ArchiveState? Archived { get; init; }

    /// <summary>Whether that archive is encrypted (a shield in place of the zipper).</summary>
    public bool ArchiveEncrypted { get; init; }

    /// <summary>How far its zipping got (0 to 1), while <see cref="Archived"/> is <c>Zipping</c>: the lock button zips what it locks.</summary>
    public double ZipProgress { get; init; }

    /// <summary>Its address, where the page asks how far its zipping got (it answers whether or not the password is unlocked).</summary>
    public string? Address { get; init; }
}
