namespace Notes.Features.Components.Create;

/// <summary>
/// The texts of the button that creates a file or directory (<see cref="_CreateButton"/>). A site with texts of its own derives from this
/// class and registers it in place of this one, as for <see cref="Toolbar.ToolbarTexts"/>.
/// </summary>
public class CreateTexts
{
    public virtual string Create(bool isFile) => isFile ? "Create file" : "Create directory";

    /// <summary>The server's, where the user may not add entries to the directory.</summary>
    public virtual string CantCreateHere(bool isFile)
        => isFile ? "Insufficient permissions to create a file here." : "Insufficient permissions to create a directory here.";
}
