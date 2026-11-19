using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Create;

public class _CreateButton : PageModel
{
    /// <summary>Whether the button creates a file (otherwise a subdirectory).</summary>
    public bool IsFile { get; init; }

    /// <summary>Why nothing can be created here, or null if it can.</summary>
    public string? DisabledReason { get; init; }

    /// <summary>Whether it says what it adds next to its icon: it is then all there is of an empty group, which has no header.</summary>
    public bool ShowsLabel { get; init; }
}
