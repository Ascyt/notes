using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Create;

public class _CreateButton : PageModel
{
    /// <summary>Whether the button creates a file (otherwise a subdirectory).</summary>
    public bool IsFile { get; init; }

    /// <summary>Why nothing can be created here, or null if it can.</summary>
    public string? DisabledReason { get; init; }
}
