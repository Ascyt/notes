using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Rename;

public class _RenameInput : PageModel
{
    /// <summary>The current name of the file or directory.</summary>
    public string Name { get; init; } = "";

    /// <summary>Why the entry can't be renamed, or null if it can. The name is then only shown (read-only), with the reason as its tooltip.</summary>
    public string? DisabledReason { get; init; }
}
