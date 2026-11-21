namespace Notes.Features.Components.Rename;

/// <summary>
/// The texts of the name field of the breadcrumb (<see cref="_RenameInput"/>) and its script. A site with texts of its own derives from this
/// class and registers it in place of this one, as for <see cref="Toolbar.ToolbarTexts"/>. The script's texts are properties, with what
/// changes in braces (<c>{name}</c>).
/// </summary>
public class RenameTexts
{
    /// <summary>The field, read out.</summary>
    public virtual string Name => "Name";

    /// <summary>The button that renames; also before why it can't be, on the field.</summary>
    public virtual string Rename => "Rename";

    /// <summary>The button that puts the name back.</summary>
    public virtual string Reset => "Reset";

    public virtual string Renamed => "Renamed to {name}.";

    /// <summary>Where the server says nothing about why.</summary>
    public virtual string RenameFailed => "Failed to rename ({status}).";
}
