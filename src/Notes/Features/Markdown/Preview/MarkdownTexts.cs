namespace Notes.Features.Markdown.Preview;

/// <summary>
/// The texts of rendered Markdown on the page of a file, and of its scripts. A site with texts of its own derives from this class and
/// registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>. The scripts' texts are properties, with what
/// changes in braces.
/// </summary>
public class MarkdownTexts
{
    public virtual string EditHere => "Edit here";
    public virtual string ViewSourceHere => "View the source here";

    // The scripts'

    public virtual string DiagramNotDrawn => "This diagram could not be drawn: {error}";

    public virtual string PrintHint
        => "For the best result, uncheck \"Print headers and footers\" in the print dialog. Use a Chromium-based browser for properly styled headers and footers.";
}
