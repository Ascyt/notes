using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.View;

public class RenameInput : PageModel
{
    /// <summary>The current name of the file or directory.</summary>
    public string Name { get; init; } = "";
}
