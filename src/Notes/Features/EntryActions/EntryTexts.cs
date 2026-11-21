namespace Notes.Features.EntryActions;

/// <summary>
/// What the server answers about an entry in general, whichever action was asked for: what isn't there, a request that makes no sense, what
/// no action may do. A site with texts of its own derives from this class and registers it in place of this one, as for
/// <see cref="Components.Toolbar.ToolbarTexts"/>. (Messages that say what the server did are for whoever reads them, so they are texts too.)
/// </summary>
public class EntryTexts
{
    public virtual string PathNotFound => "Path not found.";
    public virtual string DirectoryNotFound => "Directory not found.";
    public virtual string FileNotFound => "File not found.";

    /// <summary>A request that no page of the app makes (a missing or unknown parameter).</summary>
    public virtual string InvalidRequest => "Invalid request.";

    public virtual string MoreThanOneAction => "The address asks for more than one action.";
    public virtual string NothingToBeDone => "The address asks for nothing that can be done.";
    public virtual string RepeatedParameter => "A parameter of the address is given more than once.";
    public virtual string NoReadAccessToDirectory => "No read access to this directory.";
    public virtual string RootCantBeDeleted => "Cannot delete the root directory.";
    public virtual string RootCantBeRenamed => "Cannot rename the root directory.";
    public virtual string NameTaken => "A file or directory with that name already exists.";
}
