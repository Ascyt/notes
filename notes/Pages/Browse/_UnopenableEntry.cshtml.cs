using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.Browse;

/// <summary>
/// A row of the listing for an entry that can't be opened: one the user may not read (with a lock), or, in an earlier state, one whose
/// history can't be seen (with a crossed-out calendar). Only its name is shown, and it can't be selected.
/// </summary>
public class _UnopenableEntry : PageModel
{
    public string Name { get; init; } = "";
    public bool IsDirectory { get; init; }

    /// <summary>Why it can't be opened: <see cref="ListedKind.Locked"/> or <see cref="ListedKind.NoHistory"/>.</summary>
    public ListedKind Kind { get; init; }

    /// <summary>Its position in the listing, counted from 1.</summary>
    public int Number { get; init; }

    /// <summary>
    /// Where it leads, if anywhere: an entry without history leads to the entry as it is now, whose page says why it can't be seen as it was
    /// (unless it is gone now). A locked one leads nowhere.
    /// </summary>
    public string? Href { get; init; }

    /// <summary>The shortcut of the link, if it is one.</summary>
    public string? Key { get; init; }

    /// <summary>Whether the other rows have a box to select them, which this one then has too (disabled), to keep the names in line.</summary>
    public bool ShowsCheckbox { get; init; }
}
