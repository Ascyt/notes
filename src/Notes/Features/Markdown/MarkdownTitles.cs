using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Notes.Features.Markdown;

/// <summary>
/// The name a markdown file is shown with in place of its file name: the text of its first top-level heading (<c># Title</c>). Only for showing:
/// addresses, sorting and renaming keep the file name.
/// </summary>
public static class MarkdownTitles
{
    /// <summary>
    /// How much of the start of a file is looked at. The listing reads this much of each markdown file, so it is kept small; the page of a file
    /// looks at the same amount, so that both show the same name.
    /// </summary>
    private const int ReadLength = 16 * 1024;

    public static bool IsMarkdown(string name)
        => Path.GetExtension(name).Equals(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>The title of the markdown text, or null if its start has no top-level heading (or only an empty one).</summary>
    public static string? Of(string markdown)
    {
        // Parsed rather than searched for "# ", so that a line in a code block or a "#" without a space isn't taken for a heading. Only
        // headings of the document itself count, not one in a quote or a list.
        string start = markdown.Length > ReadLength ? markdown[..ReadLength] : markdown;
        HeadingBlock? heading = Markdig.Markdown.Parse(start).OfType<HeadingBlock>().FirstOrDefault(block => block.Level == 1);
        if (heading?.Inline is null)
            return null;

        // Its text without the markup (emphasis, links, code), as the preview shows it
        StringBuilder text = new();
        foreach (Inline inline in heading.Inline.Descendants<Inline>())
        {
            _ = inline switch
            {
                LiteralInline literal => text.Append(literal.Content),
                CodeInline code => text.Append(code.Content),
                AutolinkInline autolink => text.Append(autolink.Url),
                HtmlEntityInline entity => text.Append(entity.Transcoded),
                LineBreakInline => text.Append(' '),
                _ => text
            };
        }

        string title = text.ToString().Trim();
        return title.Length > 0 ? title : null;
    }

    /// <summary>
    /// The title of the markdown file on disk (<see cref="Of(string)"/>), or null if it has none or can't be read (it is then shown by its file
    /// name, as any other file).
    /// </summary>
    public static string? OfFile(string physicalPath)
    {
        try
        {
            using FileStream stream = new(physicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] buffer = new byte[ReadLength];
            int length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            // Not text: nothing in it is a heading
            if (buffer.AsSpan(0, length).Contains((byte)0))
                return null;
            using StreamReader reader = new(new MemoryStream(buffer, 0, length), detectEncodingFromByteOrderMarks: true);
            return Of(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
