namespace Notes.Features.Editor.InsertMedia;

/// <summary>
/// The texts of putting a file into a markdown file (<see cref="_InsertMedia"/>) and of its script. A site with texts of its own derives from
/// this class and registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>. The script's texts are properties,
/// with what changes in braces.
/// </summary>
public class InsertMediaTexts
{
    public virtual string Title => "Insert a file";
    public virtual string File => "File";
    public virtual string SavedAs => "Saved as";
    public virtual string SavedAsHelp => "Relative to this file. Directories that are not there are created.";
    public virtual string Cancel => "Cancel";
    public virtual string Insert => "Insert";

    // The script's

    public virtual string NoFileChosen => "No file has been chosen.";
    public virtual string NoFileName => "The path has no file name.";
    public virtual string OutsideOfTheNotes => "The path leads outside of the notes.";
    public virtual string Uploading => "Uploading {name}";
    public virtual string Failed => "Failed to upload {name} ({reason}).";

    /// <summary>Why the editor's text didn't get the link: the editor isn't there (yet).</summary>
    public virtual string NoEditor => "The editor has not loaded yet.";
}
