using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;
using Notes.Features.Media;

namespace Notes.Features.Markdown;

/// <summary>
/// An image of the markdown (<c>![name](clip.mp4)</c>) that is a video or audio file is shown with the browser's player instead, as raw HTML
/// (where <c>&lt;video&gt;</c> would be written) is not allowed. Which files these are goes by the extension, as for the pages of the files
/// (<see cref="MediaTypes"/>). Markdig's own media extension isn't used: it doesn't see the extension once the address has its query
/// (<see cref="MarkdownLinks.SafeUrl"/>), and gives every video a fixed size.
/// </summary>
internal sealed class MarkdownMedia : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is not HtmlRenderer htmlRenderer)
            return;
        ObjectRendererCollection renderers = htmlRenderer.ObjectRenderers;
        int index = renderers.FindIndex(candidate => candidate is LinkInlineRenderer);
        if (index >= 0)
            renderers[index] = new MediaLinkRenderer((LinkInlineRenderer)renderers[index]);
    }

    /// <summary>The kind of media the address leads to, by the extension of its path (without the query or fragment).</summary>
    public static MediaKind KindOf(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return MediaKind.None;
        int end = url.IndexOfAny(['?', '#']);
        string path = Uri.UnescapeDataString(end < 0 ? url : url[..end]);
        return MediaTypes.TryGet(path, out _, out MediaKind kind) ? kind : MediaKind.None;
    }

    private sealed class MediaLinkRenderer(LinkInlineRenderer linkRenderer) : HtmlObjectRenderer<LinkInline>
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            string? element = link.IsImage && renderer.EnableHtmlForInline
                ? KindOf(link.Url) switch
                {
                    MediaKind.Video => "video",
                    MediaKind.Audio => "audio",
                    _ => null
                }
                : null;
            if (element is null)
            {
                linkRenderer.Write(renderer, link);
                return;
            }

            // The text of the image names the player, for those who can't see it. Its address was made safe already (SafeUrl).
            string name = link.FirstChild is null ? "" : string.Concat(link.Select(child => child is LiteralInline literal ? literal.Content.ToString() : ""));
            renderer.Write('<').Write(element).Write(" controls preload=\"metadata\" src=\"").WriteEscapeUrl(link.Url).Write('"');
            if (name != "")
                renderer.Write(" aria-label=\"").WriteEscape(name).Write('"');
            if (!string.IsNullOrEmpty(link.Title))
                renderer.Write(" title=\"").WriteEscape(link.Title).Write('"');
            renderer.WriteAttributes(link).Write("></").Write(element).Write('>');
        }
    }
}
