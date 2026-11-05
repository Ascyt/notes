using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Rename;

public class _RenameInput : PageModel
{
    /// <summary>The current name of the file or directory.</summary>
    public string Name { get; init; } = "";
}
