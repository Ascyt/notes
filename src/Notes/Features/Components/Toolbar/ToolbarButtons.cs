namespace Notes.Features.Components.Toolbar;

/// <summary>
/// The buttons the pages use, so that the same button looks and behaves the same everywhere, with what they say from <see cref="ToolbarTexts"/>.
/// </summary>
public sealed class ToolbarButtons(ToolbarTexts texts)
{
    /// <param name="disabledReason">Why the file can never be saved here; without one it can be whenever there are changes.</param>
    public ToolbarButton Save(string? disabledReason = null)
        => new(Id: "save-button", Icon: "fa-solid fa-floppy-disk", Label: texts.Save, Key: "^s", Tone: ToolbarTone.Primary,
            DisabledReason: disabledReason ?? texts.NothingToSave);

    /// <summary>From the rendered markdown to its editor.</summary>
    public ToolbarButton Edit()
        => new(Id: "edit-button", Icon: "fa-solid fa-pencil", Label: texts.Edit, Key: "e", Href: "?edit");

    /// <summary>
    /// In place of <see cref="Edit"/> where the file can't be changed: from the rendered markdown to its source, in the read-only editor.
    /// </summary>
    public ToolbarButton ViewSource()
        => new(Id: "view-source-button", Icon: "fa-regular fa-file-code", Label: texts.ViewSource, Key: "e", Href: "?edit");

    /// <summary>From the editor of a markdown file to what it looks like rendered.</summary>
    public ToolbarButton Preview()
        => new(Id: "preview-button", Icon: "fa-solid fa-book", Label: texts.Preview, Key: "p", Href: "?");

    /// <summary>
    /// Uploads an image, video or any other file and writes a link to it into the markdown file where the cursor is (_InsertMedia).
    /// </summary>
    /// <param name="disabledReason">Why nothing can be added next to the file, as a hint (another path given in the dialog may still work).</param>
    public ToolbarButton InsertMedia(string? disabledReason = null)
        => new(Id: "insert-media-button", Icon: "fa-regular fa-image", Label: texts.InsertMedia, Key: "!i", DisabledReason: disabledReason);
    
    /// <summary>
    /// Indents the lines of the selection, or inserts an indent at the cursor: what Tab does in the code editor, for the simple view, where
    /// a phone has no Tab key. The page's script shows the two only in the simple view (in the code editor, Ctrl+] and Ctrl+[ are Monaco's own).
    /// </summary>
    public ToolbarButton Indent()
        => new(Id: "indent-button", Icon: "fa-solid fa-indent", Label: texts.Indent, Key: "^]", IsHidden: true);

    /// <summary>Takes an indent away from the lines of the selection, or the line of the cursor: what Shift+Tab does in the code editor.</summary>
    public ToolbarButton Outdent()
        => new(Id: "outdent-button", Icon: "fa-solid fa-outdent", Label: texts.Outdent, Key: "^[", IsHidden: true);

    /// <summary>
    /// Between the code editor (Monaco) and the simple view, a plain text field that works better on a phone. The page's script sets the
    /// icon and label to those of the view it leads to, and keeps the choice.
    /// </summary>
    public ToolbarButton SimpleView()
        => new(Id: "simple-view-button", Icon: "fa-solid fa-mobile-screen-button", Label: texts.SimpleView, Key: "!+E");
    
    /// <summary>
    /// Makes the text of the editor larger (see editorZoom in editor.js, which keeps the zoom). Plus is typed with Shift on some layouts.
    /// </summary>
    public ToolbarButton ZoomIn()
        => new(Id: "zoom-in-button", Icon: "fa-solid fa-magnifying-glass-plus", Label: texts.ZoomIn, Key: "!=");

    /// <summary>Makes the text of the editor smaller.</summary>
    public ToolbarButton ZoomOut()
        => new(Id: "zoom-out-button", Icon: "fa-solid fa-magnifying-glass-minus", Label: texts.ZoomOut, Key: "!-");

    /// <summary>
    /// From a media file to the one before it in the grid of its directory, disabled at the first. The arrow keys are left to a video or audio
    /// player that has the focus (see shortcuts.js), which seeks with them.
    /// </summary>
    /// <param name="href">The address of the media before it, or null if it is the first.</param>
    public ToolbarButton PreviousMedia(string? href)
        => new(Id: "previous-media-button", Icon: "fa-solid fa-chevron-left", Label: texts.PreviousMedia, Key: "ArrowLeft", Href: href,
            DisabledReason: href is null ? texts.NoPreviousMedia : null);

    /// <summary>From a media file to the one after it in the grid of its directory, disabled at the last.</summary>
    /// <param name="href">The address of the media after it, or null if it is the last.</param>
    public ToolbarButton NextMedia(string? href)
        => new(Id: "next-media-button", Icon: "fa-solid fa-chevron-right", Label: texts.NextMedia, Key: "ArrowRight", Href: href,
            DisabledReason: href is null ? texts.NoNextMedia : null);

    /// <summary>Shows an image, video or PDF over the whole screen (BinaryView), and leaves it again.</summary>
    public ToolbarButton Fullscreen()
        => new(Id: "fullscreen-button", Icon: "fa-solid fa-expand", Label: texts.Fullscreen, Key: "f");

    /// <summary>
    /// The bytes of a media file as the browser shows them, instead of the page around them. Every file has them (<c>?raw</c>), but only on
    /// media does the browser show more than the page already does.
    /// </summary>
    public ToolbarButton Raw()
        => new(Id: "raw-button", Icon: "fa-regular fa-file-code", Label: texts.Raw, Key: "^u", Href: "?raw");

    /// <summary>
    /// Saves the rendered markdown as a PDF, through the browser's print dialog (markdown-print.js). Ctrl+P, which prints anyway, goes
    /// through it too, so that the PDF always looks the same.
    /// </summary>
    public ToolbarButton SaveAsPdf()
        => new(Id: "pdf-button", Icon: "fa-solid fa-file-pdf", Label: texts.SaveAsPdf, Key: "^p");

    public ToolbarButton Download(bool isFile)
        => new(Id: "download-button", Icon: "fa-solid fa-download", Label: texts.Download(isFile), Key: "!PageDown", Href: "?download",
            IsDownload: true);

    /// <summary>
    /// In place of <see cref="Download"/> for a directory as it is now, for a logged-in user: opens the dialog that asks how to make the archive
    /// (_ArchiveModal).
    /// </summary>
    public ToolbarButton DownloadAsArchive()
        => new(Id: "download-button", Icon: "fa-solid fa-download", Label: texts.DownloadAsArchive, Key: "!PageDown");

    public ToolbarButton Upload(string? disabledReason = null)
        => new(Id: "upload-button", Icon: "fa-solid fa-upload", Label: texts.Upload, Key: "!PageUp", DisabledReason: disabledReason);

    /// <summary>
    /// The way to the configuration page. Only for those who manage the configuration, and not there at all for anyone else (unlike the other
    /// buttons, which are disabled with a reason): nobody else is to know that such a page exists.
    /// </summary>
    /// <param name="isFile">Whether the configuration is of a file (otherwise of a directory).</param>
    public ToolbarButton Config(bool isFile)
        => new(Id: "config-button", Icon: "fa-solid fa-gear", Label: texts.EditConfiguration(isFile), Key: "c", Href: "?config");

    /// <summary>
    /// Opens the choice of an earlier state to look at (_HistoryModal). Only for those who may see the history of the entry, and for admins,
    /// who see it disabled where the configuration leaves the entry out of the history: for everyone else there are no earlier states.
    /// </summary>
    /// <param name="disabledReason">Why there are no earlier states to see (only for admins, who may know about the configuration).</param>
    public ToolbarButton History(string? disabledReason = null)
        => new(Id: "history-button", Icon: "fa-solid fa-calendar", Label: texts.History, Key: "!H", DisabledReason: disabledReason);

    /// <summary>
    /// Shows or hides the list of the changes of the entry (_CommitSidebar), its shortcut also focuses it; in the view of a commit, where the list
    /// is always shown, it focuses it or leaves it. It is there wherever the history button is (left of it), and disabled for the same reason.
    /// </summary>
    public ToolbarButton Commits(string? disabledReason = null)
        => new(Id: "commits-button", Icon: "fa-brands fa-git-alt", Label: texts.Commits, Key: "h", DisabledReason: disabledReason);

    /// <summary>
    /// In the view of a commit, the way between its state and what it changed of the entry (docs/development/history-specification.md, "The
    /// changes of a commit"), right of the changes button.
    /// </summary>
    /// <param name="href">The address of the other view, with the commit in it (so the card's script leaves it as it is).</param>
    /// <param name="leadsToChanges">Whether it leads from the state to the changes, or back.</param>
    /// <param name="disabledReason">Why the other view can't be shown, if it can't.</param>
    public ToolbarButton CommitChanges(string href, bool leadsToChanges, string? disabledReason = null)
        => leadsToChanges
            ? new(Id: "commit-changes-button", Icon: "fa-solid fa-code-compare", Label: texts.ShowChangesOfCommit, Key: "+H", Href: href,
                DisabledReason: disabledReason)
            : new(Id: "commit-changes-button", Icon: "fa-solid fa-code-commit", Label: texts.ShowStateOfCommit, Key: "+H", Href: href,
                DisabledReason: disabledReason);

    /// <summary>
    /// Locks the entry's own password, which is unlocked now (and what is unlocked inside it), and goes to the folder it is in (POST
    /// ?lock=entry, _PasswordModal.js). Only where the entry has a password of its own that is unlocked.
    /// </summary>
    public ToolbarButton LockEntry(bool isFile)
        => new(Id: "lock-entry-button", Icon: "fa-solid fa-lock", Label: texts.LockEntry(isFile), Tone: ToolbarTone.Warning);

    /// <summary>
    /// Zips the directory, an archived folder that is unzipped now, and goes to the folder it is in (POST ?zip=entry, _Unzipping.js). Only
    /// there.
    /// </summary>
    public ToolbarButton ZipEntry(bool encrypted)
        => new(Id: "zip-entry-button", Icon: encrypted ? "fa-solid fa-shield" : "fa-solid fa-file-zipper", Label: texts.ZipEntry(encrypted),
            Tone: encrypted ? ToolbarTone.Warning : ToolbarTone.Primary);

    /// <summary>Puts the default configuration into the editor of the configuration page (it is only saved by Save).</summary>
    public ToolbarButton ResetConfig()
        => new(Id: "reset-button", Icon: "fa-solid fa-rotate-left", Label: texts.ResetConfiguration, Key: "^!r");

    /// <summary>From the configuration page back to the file or directory itself.</summary>
    public ToolbarButton BackToEntry(bool isFile, string href)
        => new(Id: "back-button", Icon: "fa-solid fa-arrow-left", Label: texts.BackToEntry(isFile), Key: "Backspace", Href: href);
}
