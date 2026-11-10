using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Toolbar;

/// <summary>What the icon of a toolbar button looks like.</summary>
public enum ToolbarTone
{
    /// <summary>The usual dark icon.</summary>
    Normal,
    /// <summary>Blue: the one thing that is worth doing now (while it can be done).</summary>
    Primary,
    /// <summary>It destroys something: usual until it is hovered or focused, then red.</summary>
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
        => new(Id: "save-button", Icon: "fa-solid fa-floppy-disk", Label: "Save", Key: "^s", OnClick: "saveFile()", Tone: ToolbarTone.Primary, DisabledReason: disabledReason ?? "There is nothing to save.");

    /// <summary>From the rendered markdown to its editor.</summary>
    public static ToolbarButton Edit()
        => new(Id: "edit-button", Icon: "fa-solid fa-pencil", Label: "Edit this file", Key: "e", Href: "?edit");

    /// <summary>From the editor of a markdown file to what it looks like rendered.</summary>
    public static ToolbarButton Preview()
        => new(Id: "preview-button", Icon: "fa-solid fa-book", Label: "Preview this file", Key: "p", Href: "?");

    /// <summary>
    /// Between the code editor (Monaco) and the simple view, a plain text field that works better on a phone. The page's script sets the
    /// icon and label to those of the view it leads to, and keeps the choice.
    /// </summary>
    public static ToolbarButton SimpleView()
        => new(Id: "simple-view-button", Icon: "fa-solid fa-mobile-screen-button", Label: "Switch to the simple editor (for phones)", Key: "!+E", OnClick: "toggleSimpleView()");

    /// <summary>Makes the text of the editor larger (see editorZoom in editor.js, which keeps the zoom). Plus is typed with Shift on some layouts.</summary>
    public static ToolbarButton ZoomIn()
        => new(Id: "zoom-in-button", Icon: "fa-solid fa-magnifying-glass-plus", Label: "Zoom in", Key: "!+ !++", OnClick: "editorZoom.zoomIn()");

    /// <summary>Makes the text of the editor smaller.</summary>
    public static ToolbarButton ZoomOut()
        => new(Id: "zoom-out-button", Icon: "fa-solid fa-magnifying-glass-minus", Label: "Zoom out", Key: "!-", OnClick: "editorZoom.zoomOut()");

    /// <summary>The bytes of a media file as the browser shows them, instead of the page around them.</summary>
    public static ToolbarButton Raw()
        => new(Id: "raw-button", Icon: "fa-regular fa-file-image", Label: "View the raw image", Key: "^u", Href: "?raw");

    public static ToolbarButton Download(bool isFile)
        => new(Id: "download-button", Icon: "fa-solid fa-download", Label: isFile ? "Download this file" : "Download this directory as a zip", Key: "!PageDown", Href: "?download", IsDownload: true);

    public static ToolbarButton Upload(string? disabledReason = null)
        => new(Id: "upload-button", Icon: "fa-solid fa-upload", Label: "Upload files", Key: "!PageUp", DisabledReason: disabledReason);

    /// <param name="kind">What is being deleted ("file" or "directory").</param>
    public static ToolbarButton Delete(string kind, string? disabledReason = null)
        => new(Id: "delete-button", Icon: "fa-regular fa-trash-can", Label: $"Delete this {kind}", Key: "Delete", OnClick: "requestDelete()", Tone: ToolbarTone.Danger, DisabledReason: disabledReason);

    /// <summary>
    /// The way to the permissions page. Only for those who manage permissions, and not there at all for anyone else (unlike the other
    /// buttons, which are disabled with a reason): nobody else is to know that such a page exists.
    /// </summary>
    /// <param name="kind">What the permissions are of ("file" or "directory").</param>
    public static ToolbarButton Permissions(string kind)
        => new(Id: "config-button", Icon: "fa-solid fa-gear", Label: $"Edit the permissions of this {kind}", Key: "c", Href: "?config");

    /// <summary>Puts the default permissions into the editor of the permissions page (they are only saved by Save).</summary>
    public static ToolbarButton ResetPermissions()
        => new(Id: "reset-button", Icon: "fa-solid fa-rotate-left", Label: "Reset to the default permissions (Save keeps them)", Key: "^!r", OnClick: "resetConfig()");

    /// <summary>From the permissions page back to the file or directory itself.</summary>
    public static ToolbarButton BackToEntry(string kind, string href)
        => new(Id: "back-button", Icon: "fa-solid fa-arrow-left", Label: $"Back to this {kind}", Key: "Backspace", Href: href);
}

public class _Toolbar : PageModel
{
    public IReadOnlyList<ToolbarButton> Buttons { get; init; } = [];
}
