using System.Text;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace notes.Features.View.Editor;

/// <summary>
/// Turns the text of a markdown file into the HTML its preview shows.
/// </summary>
/// <remarks>
/// A note is written by whoever may change it and read by everyone else, so what comes out must never be able to run a script
/// in the page: raw HTML is not allowed (it is shown as the text it is), links can only lead to places that can't run code, and the
/// attributes markdown can set (<c>{#id .class}</c>) are cut down to the harmless ones.
/// </remarks>
public static class MarkdownRenderer
{
    /// <summary>What the ids of headings (and <c>{#id}</c>) get in front of them, so that none of them can be the id of something of the page itself.</summary>
    private const string IdPrefix = "user-content-";

    /// <summary>The classes the extensions put on what they make. Any other class (from <c>{.class}</c>) is dropped.</summary>
    private static readonly HashSet<string> GeneratedClasses = ["contains-task-list", "task-list-item"];

    /// <summary>What a fenced code block puts in front of its language.</summary>
    private const string LanguageClassPrefix = "language-";

    /// <summary>Where a link may lead, apart from the notes themselves (relative addresses).</summary>
    private static readonly HashSet<string> LinkSchemes = ["http", "https", "mailto"];

    /// <summary>Where an image may come from, apart from the notes themselves.</summary>
    private static readonly HashSet<string> ImageSchemes = ["http", "https"];

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAbbreviations()
        .UseAutoIdentifiers()
        .UseAutoLinks()
        .UseDefinitionLists()
        .UseEmojiAndSmiley()
        .UseEmphasisExtras()
        .UseFootnotes()
        .UseGenericAttributes()
        .UseGridTables()
        .UseListExtras()
        .UsePipeTables()
        .UseTaskLists()
        .Build();

    public static string ToHtml(string markdown)
    {
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);
        foreach (MarkdownObject node in document.Descendants())
        {
            switch (node)
            {
                case LinkInline link:
                    link.Url = SafeUrl(link.Url, link.IsImage);
                    break;
                case AutolinkInline autolink:
                    autolink.Url = SafeUrl(autolink.Url, isImage: false);
                    break;
            }

            if (node.TryGetAttributes() is { } attributes)
                Sanitize(attributes);
        }

        return document.ToHtml(Pipeline);
    }

    private static void Sanitize(HtmlAttributes attributes)
    {
        if (attributes.Id is not null)
            attributes.Id = IdPrefix + attributes.Id;

        attributes.Classes?.RemoveAll(name => !GeneratedClasses.Contains(name) && !name.StartsWith(LanguageClassPrefix, StringComparison.Ordinal));
        attributes.Properties = null;
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
}
