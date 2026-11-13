using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace notes.Pages.Editor.Markdown;

/// <summary>The links and images of the rendered markdown: where they may lead, and which of them open a new tab.</summary>
internal static class MarkdownLinks
{
    /// <summary>Where a link may lead, apart from the notes themselves (relative addresses).</summary>
    private static readonly HashSet<string> LinkSchemes = ["http", "https", "mailto"];

    /// <summary>Where an image may come from, apart from the notes themselves.</summary>
    private static readonly HashSet<string> ImageSchemes = ["http", "https"];

    /// <summary>
    /// What a link to another site gets: it opens in a new tab, which gets no hold on this one (<c>noopener</c>) and isn't told
    /// which note it came from (<c>noreferrer</c>), as the path of a note may be private.
    /// </summary>
    private const string ExternalLinkRel = "noopener noreferrer";

    /// <summary>A link to another site opens in a new tab. One to the notes (a relative address, or this very host) stays in this one.</summary>
    public static void MarkIfExternal(MarkdownObject link, string? url, string siteHost)
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
    /// <param name="rawQuery">What an image of the notes gets as its query, to load its bytes.</param>
    public static string SafeUrl(string? url, bool isImage, string rawQuery = "raw")
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
            return "#" + MarkdownAttributes.IdPrefix + url[1..];

        // A page of the notes (an image of them too) is the file's own page. Its bytes are at ?raw.
        if (isImage && !url.StartsWith("//", StringComparison.Ordinal) && !text.Contains('?'))
        {
            int fragment = url.IndexOf('#');
            return fragment < 0 ? url + "?" + rawQuery : url.Insert(fragment, "?" + rawQuery);
        }

        return url;
    }
}
