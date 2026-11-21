namespace Notes.Features.Components.Breadcrumb;

/// <summary>
/// The texts of the breadcrumb (<see cref="_Breadcrumb"/>). A site with texts of its own derives from this class and registers it in place of
/// this one, as for <see cref="Toolbar.ToolbarTexts"/>.
/// </summary>
public class BreadcrumbTexts
{
    /// <summary>What the start is called where it is the root (elsewhere it is the name of the directory the site shows).</summary>
    public virtual string Root => "root";

    /// <summary>The "…" that stands for the outer directories once they don't fit.</summary>
    public virtual string DirectoriesAbove => "Directories above";
}
