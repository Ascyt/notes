using Markdig;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Mathematics;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.ColorCode;

namespace notes.Pages.Editor.Markdown;

/// <summary>The HTML of a markdown file, and which scripts of the page it needs to be shown in full.</summary>
/// <param name="Html">What the preview shows.</param>
/// <param name="HasMath">Whether there is math (<c>$...$</c>, <c>$$...$$</c>), which KaTeX renders in the browser.</param>
/// <param name="HasDiagrams">Whether there are <c>mermaid</c> code blocks, which mermaid renders in the browser.</param>
/// <param name="HasAbbreviations">Whether there are abbreviations, which the page makes show what they stand for when tapped or focused.</param>
public sealed record RenderedMarkdown(string Html, bool HasMath, bool HasDiagrams, bool HasAbbreviations);

/// <summary>
/// Turns the text of a markdown file into the HTML its preview shows.
/// </summary>
/// <remarks>
/// A note is written by whoever may change it and read by everyone else, so what comes out must never be able to run a script
/// in the page: raw HTML is not allowed (it is shown as the text it is), links can only lead to places that can't run code
/// (<see cref="MarkdownLinks"/>), and the attributes markdown can set (<c>{#id .class}</c>) are cut down to the harmless ones
/// (<see cref="MarkdownAttributes"/>).
/// Markdig's own diagram extension is not used, as it writes the text of the block into the page unescaped: a <c>mermaid</c> block
/// stays an ordinary (escaped) code block, whose text the page hands to mermaid.
/// </remarks>
public static class MarkdownRenderer
{
    /// <summary>The language of a fenced code block that is a diagram.</summary>
    private const string DiagramLanguage = "mermaid";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAbbreviations()
        .UseAlertBlocks()
        .UseAutoIdentifiers()
        .UseAutoLinks()
        .UseCitations()
        .UseCustomContainers()
        .UseDefinitionLists()
        .UseEmojiAndSmiley(enableSmileys: false) // :joy: becomes an emoji, but :) stays as written
        .UseEmphasisExtras()
        .UseFigures()
        .UseFooters()
        .UseFootnotes()
        .UseGridTables()
        .UseListExtras()
        .UseMathematics()
        .UsePipeTables()
        .UseSoftlineBreakAsHardlineBreak() // a line break in the text is one in the preview, as notes are written line by line
        .UseTaskLists()
        // Highlighted on the server, with classes that markdown.css colors in both themes. Languages it doesn't know stay plain code blocks.
        .UseColorCode(HtmlFormatterType.Css)
        .Use<SourcePositions.CodeBlockExtension>() // after the highlighting, whose output it puts into an element of its own
        .UseGenericAttributes() // last, as Markdig asks
        .Build();

    /// <param name="markdown">The text of the file.</param>
    /// <param name="siteHost">The host the page is served from: links to it stay in the tab, links elsewhere open a new one.</param>
    /// <param name="rawQuery">What an image of the notes gets as its query, to load its bytes (of the state that is shown).</param>
    public static RenderedMarkdown Render(string markdown, string siteHost, string rawQuery = "raw")
    {
        MarkdownDocument document = Markdig.Markdown.Parse(markdown, Pipeline);
        MarkdownLists.SplitAtBlankLines(document, markdown);
        SourcePositions positions = new(markdown);
        bool hasMath = false, hasDiagrams = false, hasAbbreviations = false;
        foreach (MarkdownObject node in document.Descendants())
        {
            if (node.TryGetAttributes() is { } attributes)
                MarkdownAttributes.Sanitize(attributes);

            switch (node)
            {
                case LinkInline link:
                    link.Url = MarkdownLinks.SafeUrl(link.Url, link.IsImage, rawQuery);
                    if (!link.IsImage)
                        MarkdownLinks.MarkIfExternal(link, link.Url, siteHost);
                    break;
                case AutolinkInline autolink:
                    autolink.Url = MarkdownLinks.SafeUrl(autolink.Url, isImage: false);
                    MarkdownLinks.MarkIfExternal(autolink, autolink.Url, siteHost);
                    break;
                case CustomContainer container:
                    MarkdownAttributes.AddContainerClasses(container);
                    break;
                case MathInline or MathBlock:
                    hasMath = true;
                    break;
                case AbbreviationInline:
                    hasAbbreviations = true;
                    break;
                case FencedCodeBlock code when string.Equals(code.Info, DiagramLanguage, StringComparison.OrdinalIgnoreCase):
                    hasDiagrams = true;
                    break;
            }

            if (node is Block block)
                positions.Mark(block);
        }

        return new RenderedMarkdown(document.ToHtml(Pipeline), hasMath, hasDiagrams, hasAbbreviations);
    }
}
