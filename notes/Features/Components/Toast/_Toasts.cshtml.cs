using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Toast;

public class _Toasts : PageModel
{
    /// <summary>How long (in milliseconds) a message stays when nothing says otherwise.</summary>
    public int DefaultDurationMs { get; init; } = 4000;

    /// <summary>How long errors stay: longer, because there is more to read and more to do about them.</summary>
    public int ErrorDurationMs { get; init; } = 8000;
}
