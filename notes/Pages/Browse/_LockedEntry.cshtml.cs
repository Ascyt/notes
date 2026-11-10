using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.Browse;

/// <summary>A row of the listing for an entry the user may not read: only its name, which can't be opened or selected.</summary>
public class _LockedEntry : PageModel
{
    public string Name { get; init; } = "";
    public bool IsDirectory { get; init; }

    /// <summary>Its position in the listing, counted from 1.</summary>
    public int Number { get; init; }
}
