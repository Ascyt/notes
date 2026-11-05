using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Create;

public class _CreateButton : PageModel
{
    /// <summary>Whether the button creates a file (otherwise a subdirectory).</summary>
    public bool IsFile { get; init; }
}
