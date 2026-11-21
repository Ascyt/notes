using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Components.Rename;

public class _RenameInput : PageModel
{
    /// <summary>The styles and the script of the field, by their paths in the library (see <see cref="Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Components/Rename/_RenameInput.css";
    public const string Script = "Features/Components/Rename/_RenameInput.js";

    /// <summary>The current name of the file or directory.</summary>
    public string Name { get; init; } = "";

    /// <summary>Why the entry can't be renamed, or null if it can. The name is then only shown (read-only), with the reason as its tooltip.</summary>
    public string? DisabledReason { get; init; }
}
