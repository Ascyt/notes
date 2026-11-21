namespace Notes.Features.Editor;

/// <summary>
/// The texts of the editors: the page of a file (<see cref="Pages.Editor.FileView"/>, with its binary view), the code editor that it shares
/// with the configuration page and the changes of a commit, and what their scripts say. A site with texts of its own derives from this class
/// and registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>. The scripts' texts are properties, with
/// what changes in braces.
/// </summary>
public class EditorTexts
{
    /// <summary>The simple editor, read out.</summary>
    public virtual string FileContents => "File contents";

    public virtual string FileIsEmpty => "This file is empty.";

    public virtual string TooLarge(string size, string maxTextSize)
        => $"This file is too large ({size}) to be shown here (text is shown up to {maxTextSize}). It may be downloaded using the download button above.";

    public virtual string Binary(string size)
        => $"This file is binary ({size}) and cannot be viewed here. It may be downloaded using the download button above.";

    // The server's, when a file is saved

    public virtual string CantChange => "Insufficient permissions to change this file.";
    public virtual string TooLongToSave(string maxSize) => $"The text is too long to be saved (at most {maxSize}).";
    public virtual string NotUtf8 => "The text isn't valid UTF-8. Nothing was saved.";
    public virtual string GoneWhileSaving => "The file was renamed, moved or deleted in the meantime. Nothing was saved.";
    public virtual string InUse => "The file is in use and couldn't be replaced. Nothing was saved.";

    // The scripts'

    public virtual string NothingToSave => "There is nothing to save.";

    /// <summary>Where the server says nothing about why.</summary>
    public virtual string SaveFailed => "Failed to save file ({status}).";

    public virtual string UnsavedBeforeRenaming => "The file has unsaved changes, which have to be saved before renaming.";
    public virtual string ZoomedInFully => "The editor is zoomed in as far as it goes.";
    public virtual string ZoomedOutFully => "The editor is zoomed out as far as it goes.";
    public virtual string CodeEditorIndents => "The code editor indents with Tab.";
    public virtual string SwitchToCodeEditor => "Switch to the code editor";
    public virtual string SwitchToSimpleEditor => "Switch to the simple editor (for phones)";

    /// <summary>Why the fullscreen button of a file that isn't text is disabled.</summary>
    public virtual string CantBeShown => "The file can't be shown.";

    public virtual string NoFullscreen => "This browser can't show it in fullscreen.";
}
