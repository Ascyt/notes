using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Selection;

public class _SelectionBar : PageModel
{
    /// <summary>Path of the directory being viewed, as the listing names it ("" for the root). Copying or moving puts things into it.</summary>
    public string DirectoryPath { get; init; } = "";

    /// <summary>The selection when the page is made.</summary>
    public SelectionSnapshot Selection { get; init; } = SelectionSnapshot.Empty;

    /// <summary>Why nothing can be copied or moved into the directory, or null if it can.</summary>
    public string? CreateDeniedReason { get; init; }
}
