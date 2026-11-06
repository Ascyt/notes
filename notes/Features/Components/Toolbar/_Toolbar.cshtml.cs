using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Toolbar;

/// <summary>What the icon of a toolbar button looks like.</summary>
public enum ToolbarTone
{
    /// <summary>The usual dark icon.</summary>
    Normal,
    /// <summary>Blue: the one thing that is worth doing now (while it can be done).</summary>
    Primary,
    /// <summary>Red: it destroys something.</summary>
    Danger
}

/// <summary>An icon of the toolbar, which works as a button. It is always there: when it can't be used it is disabled (with the reason) or shows a spinner, never hidden.</summary>
/// <param name="Id">Id of the element, for the scripts of the page to find it.</param>
/// <param name="Icon">Font Awesome classes of the icon, e.g. "fa-solid fa-download".</param>
/// <param name="Label">What the button does. Shown as its tooltip, as there is no text on it.</param>
/// <param name="Key">Shortcut (see shortcuts.js), if any.</param>
/// <param name="Href">Makes the button a link to this address.</param>
/// <param name="OnClick">Script to run when clicked. Buttons that the page's scripts attach their own listener to need none.</param>
/// <param name="IsDownload">Whether the link downloads what it points at.</param>
/// <param name="Tone">Colors the icon.</param>
/// <param name="DisabledReason">Disables the button from the start, and is what its tooltip says. Scripts change it with setButtonDisabled.</param>
public sealed record ToolbarButton(
    string Id,
    string Icon,
    string Label,
    string? Key = null,
    string? Href = null,
    string? OnClick = null,
    bool IsDownload = false,
    ToolbarTone Tone = ToolbarTone.Normal,
    string? DisabledReason = null)
{
    // The buttons the pages use, so that the same button looks and behaves the same everywhere

    /// <param name="disabledReason">Why the file can never be saved here; without one it can be whenever there are changes.</param>
    public static ToolbarButton Save(string? disabledReason = null)
        => new("save-button", "fa-solid fa-floppy-disk", "Save", "^S", OnClick: "saveFile()", Tone: ToolbarTone.Primary, DisabledReason: disabledReason ?? "There is nothing to save.");

    public static ToolbarButton Download(bool isFile)
        => new("download-button", "fa-solid fa-download", isFile ? "Download this file" : "Download this directory as a zip", "d", Href: "?download", IsDownload: true);

    public static ToolbarButton Upload(string? disabledReason = null)
        => new("upload-button", "fa-solid fa-upload", "Upload files", "u", DisabledReason: disabledReason);

    /// <param name="kind">What is being deleted ("file" or "directory").</param>
    public static ToolbarButton Delete(string kind, string? disabledReason = null)
        => new("delete-button", "fa-regular fa-trash-can", $"Delete this {kind}", "Delete", OnClick: "requestDelete()", Tone: ToolbarTone.Danger, DisabledReason: disabledReason);
}

public class _Toolbar : PageModel
{
    public IReadOnlyList<ToolbarButton> Buttons { get; init; } = [];
}
