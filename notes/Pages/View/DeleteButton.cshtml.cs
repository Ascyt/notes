using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.View;

public class DeleteButton : PageModel
{
    /// <summary>Whether deleting asks for confirmation by default. A page may override this by defining window.needsDeleteConfirmation().</summary>
    public bool NeedsConfirmation { get; init; }

    /// <summary>What is being deleted ("file" or "directory"), used in the confirmation text.</summary>
    public string Kind { get; init; } = "";
}
