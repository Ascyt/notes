namespace Notes.Features.Markdown.Preview;

/// <summary>The styles and scripts of rendered Markdown, by their paths in the library (see <see cref="Components.Assets.ComponentAssets"/>).</summary>
public static class MarkdownAssets
{
    public const string Styles = "Features/Markdown/Preview/markdown.css";

    /// <summary>Math, diagrams and abbreviations, for a file that has any.</summary>
    public const string Rendering = "Features/Markdown/Preview/markdown.js";

    /// <summary>Saving as PDF through the print dialog.</summary>
    public const string Print = "Features/Markdown/Preview/markdown-print.js";

    /// <summary>The button next to the text that opens the editor where it is written.</summary>
    public const string EditHere = "Features/Markdown/Preview/markdown-edit-here.js";

    /// <summary>Where the preview opens: the place the editor was left at.</summary>
    public const string StartPosition = "Features/Markdown/Preview/markdown-start-position.js";
}
