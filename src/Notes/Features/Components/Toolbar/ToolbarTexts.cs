namespace Notes.Features.Components.Toolbar;

/// <summary>
/// The texts of the buttons of the toolbar (<see cref="ToolbarButtons"/>): what each one does, shown as its tooltip and read out. A site with
/// texts of its own derives from this class, overrides what it says differently and registers its class in place of this one
/// (docs/library.md): <c>builder.Services.AddSingleton&lt;ToolbarTexts, SiteToolbarTexts&gt;()</c>.
/// </summary>
public class ToolbarTexts
{
    public virtual string Save => "Save";

    /// <summary>Why Save is disabled while there are no changes.</summary>
    public virtual string NothingToSave => "There is nothing to save.";

    public virtual string Edit => "Edit this file";
    public virtual string ViewSource => "View the source of this file";
    public virtual string Preview => "Preview this file";

    /// <summary>The editor's script (file-editor.js) changes it to the label of the view it leads to.</summary>
    public virtual string SimpleView => "Switch to the simple editor (for phones)";

    public virtual string Indent => "Indent";
    public virtual string Outdent => "Outdent";
    public virtual string InsertMedia => "Insert an image or another file";
    public virtual string ZoomIn => "Zoom in";
    public virtual string ZoomOut => "Zoom out";
    public virtual string PreviousMedia => "Previous media file";
    public virtual string NextMedia => "Next media file";
    public virtual string NoPreviousMedia => "This is the first media file of the directory.";
    public virtual string NoNextMedia => "This is the last media file of the directory.";
    public virtual string Fullscreen => "Fullscreen";
    public virtual string Raw => "View the raw file";
    public virtual string SaveAsPdf => "Save as PDF (print)";

    public virtual string Download(bool isFile)
        => isFile ? "Download this file" : "Download this directory as a zip";

    public virtual string DownloadAsArchive => "Download this directory as an archive";
    public virtual string LockEntry(bool isFile) => isFile ? "Lock this file again (its password)" : "Lock this directory again (its password)";

    public virtual string ZipEntry(bool encrypted)
        => encrypted ? "Zip and encrypt this directory now" : "Zip this directory now";

    public virtual string Upload => "Upload files";

    public virtual string EditConfiguration(bool isFile)
        => isFile ? "Edit the configuration of this file" : "Edit the configuration of this directory";

    public virtual string History => "View an earlier state";
    public virtual string Commits => "Show the changes";
    public virtual string ShowChangesOfCommit => "Show what this commit changed";
    public virtual string ShowStateOfCommit => "Show the state of this commit";
    public virtual string ResetConfiguration => "Reset to the default configuration (Save keeps it)";

    public virtual string BackToEntry(bool isFile)
        => isFile ? "Back to this file" : "Back to this directory";
}
