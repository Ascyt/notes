namespace Notes.Pages.ConfigEditor;

/// <summary>
/// The texts of the configuration page (<see cref="ConfigEditorView"/>) and its script. A site with texts of its own derives from this
/// class and registers it in place of this one, as for <see cref="Features.Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class ConfigEditorTexts
{
    public virtual string RenameDisabled => "Leave the configuration to rename it.";

    public virtual string ConfigurationOf(bool isFile)
        => isFile ? "Configuration of this file: who may do what with it." : "Configuration of this directory: who may do what with it.";

    public virtual string OfAFile => "This file's entry in the configuration of its folder, without the name.";
    public virtual string OfADirectory => "Everything in this directory has it as well, unless it says otherwise.";
    public virtual string AdminsMayDoEverything => "Admins may always do everything.";
    public virtual string OnDisk => "On disk:";

    // The script's

    public virtual string NothingToSave => "There is nothing to save.";
    public virtual string NotValid => "The configuration is not valid.";

    /// <summary>Where the server says nothing about why.</summary>
    public virtual string SaveFailed => "Failed to save ({status}).";
}
