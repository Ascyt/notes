using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.View;

public class _Download : PageModel
{
    /// <summary>Whether the current entry is a file (downloaded as is) rather than a directory (downloaded as a zip).</summary>
    public bool IsFile { get; init; }
}
