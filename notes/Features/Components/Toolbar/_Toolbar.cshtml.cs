using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Toolbar;

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
    bool IsHidden = false)
{
    // The buttons the pages use, so that the same button looks and behaves the same everywhere

    /// <param name="disabledReason">Why the file can never be saved here; without one it can be whenever there are changes.</param>
    public static ToolbarButton Save(string? disabledReason = null)
        => new(Id: "save-button", Icon: "fa-solid fa-floppy-disk", Label: "Save", Key: "^s", Tone: ToolbarTone.Primary, DisabledReason: disabledReason ?? "There is nothing to save.");

    /// <summary>From the rendered markdown to its editor.</summary>
    public static ToolbarButton Edit()
        => new(Id: "edit-button", Icon: "fa-solid fa-pencil", Label: "Edit this file", Key: "e", Href: "?edit");

    /// <summary>In place of <see cref="Edit"/> where the file can't be changed: from the rendered markdown to its source, in the read-only editor.</summary>
    public static ToolbarButton ViewSource()
        => new(Id: "view-source-button", Icon: "fa-regular fa-file-code", Label: "View the source of this file", Key: "e", Href: "?edit");

    /// <summary>From the editor of a markdown file to what it looks like rendered.</summary>
    public static ToolbarButton Preview()
        => new(Id: "preview-button", Icon: "fa-solid fa-book", Label: "Preview this file", Key: "p", Href: "?");

    /// <summary>
    /// Between the code editor (Monaco) and the simple view, a plain text field that works better on a phone. The page's script sets the
    /// icon and label to those of the view it leads to, and keeps the choice.
    /// </summary>
    public static ToolbarButton SimpleView()
        => new(Id: "simple-view-button", Icon: "fa-solid fa-mobile-screen-button", Label: "Switch to the simple editor (for phones)", Key: "!+E");

    /// <summary>
    /// Indents the lines of the selection, or inserts an indent at the cursor: what Tab does in the code editor, for the simple view, where
    /// a phone has no Tab key. The page's script shows the two only in the simple view (in the code editor, Ctrl+] and Ctrl+[ are Monaco's own).
    /// </summary>
    public static ToolbarButton Indent()
        => new(Id: "indent-button", Icon: "fa-solid fa-indent", Label: "Indent", Key: "^]", IsHidden: true);

    /// <summary>Takes an indent away from the lines of the selection, or the line of the cursor: what Shift+Tab does in the code editor.</summary>
    public static ToolbarButton Outdent()
        => new(Id: "outdent-button", Icon: "fa-solid fa-outdent", Label: "Outdent", Key: "^[", IsHidden: true);

    /// <summary>Makes the text of the editor larger (see editorZoom in editor.js, which keeps the zoom). Plus is typed with Shift on some layouts.</summary>
    public static ToolbarButton ZoomIn()
        => new(Id: "zoom-in-button", Icon: "fa-solid fa-magnifying-glass-plus", Label: "Zoom in", Key: "!=");

    /// <summary>Makes the text of the editor smaller.</summary>
    public static ToolbarButton ZoomOut()
        => new(Id: "zoom-out-button", Icon: "fa-solid fa-magnifying-glass-minus", Label: "Zoom out", Key: "!-");

    /// <summary>
    /// From a media file to the one before it in the grid of its directory, disabled at the first. The arrow keys are left to a video or audio
    /// player that has the focus (see shortcuts.js), which seeks with them.
    /// </summary>
    /// <param name="href">The address of the media before it, or null if it is the first.</param>
    public static ToolbarButton PreviousMedia(string? href)
        => new(Id: "previous-media-button", Icon: "fa-solid fa-chevron-left", Label: "Previous media file", Key: "ArrowLeft", Href: href,
            DisabledReason: href is null ? "This is the first media file of the directory." : null);

    /// <summary>From a media file to the one after it in the grid of its directory, disabled at the last.</summary>
    /// <param name="href">The address of the media after it, or null if it is the last.</param>
    public static ToolbarButton NextMedia(string? href)
        => new(Id: "next-media-button", Icon: "fa-solid fa-chevron-right", Label: "Next media file", Key: "ArrowRight", Href: href,
            DisabledReason: href is null ? "This is the last media file of the directory." : null);

    /// <summary>Shows an image, video or PDF over the whole screen (BinaryView), and leaves it again.</summary>
    public static ToolbarButton Fullscreen()
        => new(Id: "fullscreen-button", Icon: "fa-solid fa-expand", Label: "Fullscreen", Key: "f");

    /// <summary>
    /// The bytes of a media file as the browser shows them, instead of the page around them. Every file has them (<c>?raw</c>), but only on
    /// media does the browser show more than the page already does.
    /// </summary>
    public static ToolbarButton Raw()
        => new(Id: "raw-button", Icon: "fa-regular fa-file-code", Label: "View the raw file", Key: "^u", Href: "?raw");

    /// <summary>
    /// Saves the rendered markdown as a PDF, through the browser's print dialog (markdown-print.js). Ctrl+P, which prints anyway, goes
    /// through it too, so that the PDF always looks the same.
    /// </summary>
    public static ToolbarButton SaveAsPdf()
        => new(Id: "pdf-button", Icon: "fa-solid fa-file-pdf", Label: "Save as PDF (print)", Key: "^p");

    public static ToolbarButton Download(bool isFile)
        => new(Id: "download-button", Icon: "fa-solid fa-download", Label: isFile ? "Download this file" : "Download this directory as a zip", Key: "!PageDown", Href: "?download", IsDownload: true);

    public static ToolbarButton Upload(string? disabledReason = null)
        => new(Id: "upload-button", Icon: "fa-solid fa-upload", Label: "Upload files", Key: "!PageUp", DisabledReason: disabledReason);

    /// <summary>
    /// The way to the configuration page. Only for those who manage the configuration, and not there at all for anyone else (unlike the other
    /// buttons, which are disabled with a reason): nobody else is to know that such a page exists.
    /// </summary>
    /// <param name="kind">What the configuration is of ("file" or "directory").</param>
    public static ToolbarButton Config(string kind)
        => new(Id: "config-button", Icon: "fa-solid fa-gear", Label: $"Edit the configuration of this {kind}", Key: "c", Href: "?config");

    /// <summary>
    /// Opens the choice of an earlier state to look at (_HistoryModal). Only for those who may see the history of the entry, and for admins,
    /// who see it disabled where the configuration leaves the entry out of the history: for everyone else there are no earlier states.
    /// </summary>
    /// <param name="disabledReason">Why there are no earlier states to see (only for admins, who may know about the configuration).</param>
    public static ToolbarButton History(string? disabledReason = null)
        => new(Id: "history-button", Icon: "fa-solid fa-calendar", Label: "View an earlier state", Key: "!H", DisabledReason: disabledReason);

    /// <summary>
    /// Shows or hides the list of the changes of the entry (_CommitSidebar), its shortcut also focuses it; in the view of a commit, where the list
    /// is always shown, it focuses it or leaves it. It is there wherever the history button is (left of it), and disabled for the same reason.
    /// </summary>
    public static ToolbarButton Commits(string? disabledReason = null)
        => new(Id: "commits-button", Icon: "fa-brands fa-git-alt", Label: "Show the changes", Key: "h", DisabledReason: disabledReason);

    /// <summary>
    /// In the view of a commit, the way between its state and what it changed of the entry (docs/development/history-specification.md, "The
    /// changes of a commit"), right of the changes button.
    /// </summary>
    /// <param name="href">The address of the other view, with the commit in it (so the card's script leaves it as it is).</param>
    /// <param name="leadsToChanges">Whether it leads from the state to the changes, or back.</param>
    /// <param name="disabledReason">Why the other view can't be shown, if it can't.</param>
    public static ToolbarButton CommitChanges(string href, bool leadsToChanges, string? disabledReason = null)
        => leadsToChanges
            ? new(Id: "commit-changes-button", Icon: "fa-solid fa-code-compare", Label: "Show what this commit changed", Key: "+H", Href: href, DisabledReason: disabledReason)
            : new(Id: "commit-changes-button", Icon: "fa-solid fa-code-commit", Label: "Show the state of this commit", Key: "+H", Href: href, DisabledReason: disabledReason);

    /// <summary>Puts the default configuration into the editor of the configuration page (it is only saved by Save).</summary>
    public static ToolbarButton ResetConfig()
        => new(Id: "reset-button", Icon: "fa-solid fa-rotate-left", Label: "Reset to the default configuration (Save keeps it)", Key: "^!r");

    /// <summary>From the configuration page back to the file or directory itself.</summary>
    public static ToolbarButton BackToEntry(string kind, string href)
        => new(Id: "back-button", Icon: "fa-solid fa-arrow-left", Label: $"Back to this {kind}", Key: "Backspace", Href: href);
}

public class _Toolbar : PageModel
{
    public IReadOnlyList<ToolbarButton> Buttons { get; init; } = [];
}
