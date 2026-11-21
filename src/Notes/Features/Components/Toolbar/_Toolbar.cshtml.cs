using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Components.Toolbar;

/// <summary>What the icon of a toolbar button looks like.</summary>
public enum ToolbarTone
{
    /// <summary>The usual dark icon.</summary>
    Normal,
    /// <summary>Blue: the one thing that is worth doing now (while it can be done).</summary>
    Primary
}

/// <summary>
/// An icon of the toolbar, which works as a button. What the user may not do at all is left out by the page; a button that is there but can't
/// be used right now is disabled (with the reason) or shows a spinner.
/// </summary>
/// <param name="Id">Id of the element, for the scripts of the page to find it. A button without <paramref name="Href"/> does what the script
/// of the page attaches to it (<c>onClickOf</c> in site.js).</param>
/// <param name="Icon">Font Awesome classes of the icon, e.g. "fa-solid fa-download".</param>
/// <param name="Label">What the button does. Shown as its tooltip, as there is no text on it.</param>
/// <param name="Key">Shortcut (see shortcuts.js), if any.</param>
/// <param name="Href">Makes the button a link to this address.</param>
/// <param name="IsDownload">Whether the link downloads what it points at.</param>
/// <param name="Tone">Colors the icon.</param>
/// <param name="DisabledReason">Disables the button from the start, and is what its tooltip says. Scripts change it with setButtonDisabled.</param>
/// <param name="IsHidden">Hides the button from the start, for one that only belongs to a state of the page that its script shows it in.</param>
public sealed record ToolbarButton(
    string Id,
    string Icon,
    string Label,
    string? Key = null,
    string? Href = null,
    bool IsDownload = false,
    ToolbarTone Tone = ToolbarTone.Normal,
    string? DisabledReason = null,
    bool IsHidden = false);

public class _Toolbar : PageModel
{
    /// <summary>The styles of the toolbar, by their path in the library (see <see cref="Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Components/Toolbar/_Toolbar.css";

    public IReadOnlyList<ToolbarButton> Buttons { get; init; } = [];
}
