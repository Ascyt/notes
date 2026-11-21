using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Components.Toast;

public class _Toasts : PageModel
{
    /// <summary>The styles and the script of the stack, by their paths in the library (see <see cref="Notes.Features.Components.Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Components/Toast/_Toasts.css";
    public const string Script = "Features/Components/Toast/_Toasts.js";

    /// <summary>How long (in milliseconds) a message stays when nothing says otherwise.</summary>
    public int DefaultDurationMs { get; init; } = 4000;

    /// <summary>How long errors stay: longer, because there is more to read and more to do about them.</summary>
    public int ErrorDurationMs { get; init; } = 8000;
}
