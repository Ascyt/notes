using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Mathematics;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.ColorCode;

namespace notes.Features.View.Editor;

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
/// in the page: raw HTML is not allowed (it is shown as the text it is), links can only lead to places that can't run code, and the
/// attributes markdown can set (<c>{#id .class}</c>) are cut down to the harmless ones.
/// Markdig's own diagram extension is not used, as it writes the text of the block into the page unescaped: a <c>mermaid</c> block
/// stays an ordinary (escaped) code block, whose text the page hands to mermaid.
/// </remarks>
public static partial class MarkdownRenderer
{
    /// <summary>What the ids of headings (and <c>{#id}</c>) get in front of them, so that none of them can be the id of something of the page itself.</summary>
    private const string IdPrefix = "user-content-";

    /// <summary>The classes the extensions put on what they make. Any other class (from <c>{.class}</c>) is dropped.</summary>
    private static readonly HashSet<string> GeneratedClasses = ["contains-task-list", "task-list-item", "math"];

    /// <summary>Beginnings of classes the extensions make, which nothing of the page itself uses: a fenced code block's language, an alert's kind.</summary>
    private static readonly string[] GeneratedClassPrefixes = ["language-", "markdown-alert"];

    /// <summary>
    /// The class of a custom container (<c>:::name</c>). Its name becomes a second class behind this one (<c>markdown-container-name</c>),
    /// so that it can't be a class of the page.
    /// </summary>
    private const string ContainerClass = "markdown-container";

    /// <summary>The language of a fenced code block that is a diagram.</summary>
    private const string DiagramLanguage = "mermaid";

    /// <summary>Where a link may lead, apart from the notes themselves (relative addresses).</summary>
    private static readonly HashSet<string> LinkSchemes = ["http", "https", "mailto"];

    /// <summary>Where an image may come from, apart from the notes themselves.</summary>
    private static readonly HashSet<string> ImageSchemes = ["http", "https"];

    /// <summary>
    /// What a link to another site gets: it opens in a new tab, which gets no hold on this one (<c>noopener</c>) and isn't told
    /// which note it came from (<c>noreferrer</c>), as the path of a note may be private.
    /// </summary>
    private const string ExternalLinkRel = "noopener noreferrer";

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
        .UseGenericAttributes() // last, as Markdig asks
        .Build();

    /// <param name="markdown">The text of the file.</param>
    /// <param name="siteHost">The host the page is served from: links to it stay in the tab, links elsewhere open a new one.</param>
    public static RenderedMarkdown Render(string markdown, string siteHost)
    {
        MarkdownDocument document = Markdig.Markdown.Parse(markdown, Pipeline);
        bool hasMath = false, hasDiagrams = false, hasAbbreviations = false;
        foreach (MarkdownObject node in document.Descendants())
        {
            if (node.TryGetAttributes() is { } attributes)
                Sanitize(attributes);

            switch (node)
            {
                case LinkInline link:
                    link.Url = SafeUrl(link.Url, link.IsImage);
                    if (!link.IsImage)
                        MarkIfExternal(link, link.Url, siteHost);
                    break;
                case AutolinkInline autolink:
                    autolink.Url = SafeUrl(autolink.Url, isImage: false);
                    MarkIfExternal(autolink, autolink.Url, siteHost);
                    break;
                case CustomContainer container:
                    container.GetAttributes().AddClass(ContainerClass);
                    if (ContainerName().IsMatch(container.Info ?? ""))
                        container.GetAttributes().AddClass(ContainerClass + "-" + container.Info!.ToLowerInvariant());
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
        }

        return new RenderedMarkdown(document.ToHtml(Pipeline), hasMath, hasDiagrams, hasAbbreviations);
    }

    private static void Sanitize(HtmlAttributes attributes)
    {
        if (attributes.Id is not null)
            attributes.Id = IdPrefix + attributes.Id;

        attributes.Classes?.RemoveAll(name => !GeneratedClasses.Contains(name)
                                              && !GeneratedClassPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
        attributes.Properties = null;
    }

    /// <summary>A link to another site opens in a new tab. One to the notes (a relative address, or this very host) stays in this one.</summary>
    private static void MarkIfExternal(MarkdownObject link, string? url, string siteHost)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            // "//host/path" has no scheme of its own, but leads to a host all the same
            if (url is null || !url.StartsWith("//", StringComparison.Ordinal) || !Uri.TryCreate("https:" + url, UriKind.Absolute, out uri))
                return;
        }

        if (string.Equals(uri.Host, siteHost, StringComparison.OrdinalIgnoreCase))
            return;

        HtmlAttributes attributes = link.GetAttributes();
        attributes.AddProperty("target", "_blank");
        attributes.AddProperty("rel", ExternalLinkRel);
    }

    /// <summary>The address as it may be used in the page: unchanged if it is harmless, otherwise nothing.</summary>
    private static string SafeUrl(string? url, bool isImage)
    {
        if (string.IsNullOrEmpty(url))
            return "";

        // Browsers ignore whitespace and control characters inside the scheme ("java\tscript:"), so those are looked past as well
        StringBuilder compact = new(url.Length);
        foreach (char c in url)
            if (c > ' ' && c != '\u007f')
                compact.Append(c);

        string text = compact.ToString();
        int colon = text.IndexOf(':');
        int firstOther = text.AsSpan().IndexOfAny('/', '?', '#');
        bool hasScheme = colon > 0 && (firstOther < 0 || colon < firstOther);
        if (hasScheme)
            return (isImage ? ImageSchemes : LinkSchemes).Contains(text[..colon].ToLowerInvariant()) ? url : "";

        // A link to a heading of this page leads to the id it was given
        if (!isImage && url.StartsWith('#') && url.Length > 1)
            return "#" + IdPrefix + url[1..];

        // A page of the notes (an image of them too) is the file's own page. Its bytes are at ?raw.
        if (isImage && !url.StartsWith("//", StringComparison.Ordinal) && !text.Contains('?'))
        {
            int fragment = url.IndexOf('#');
            return fragment < 0 ? url + "?raw" : url.Insert(fragment, "?raw");
        }

        return url;
    }

    /// <summary>What a custom container's name may be to become a class: anything else gets none.</summary>
    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex ContainerName();
}
