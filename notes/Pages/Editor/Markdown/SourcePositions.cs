using System.Globalization;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace notes.Pages.Editor.Markdown;

/// <summary>
/// Marks the blocks of the rendered markdown with where they start in the text (line and column, both counted from 1), so that the
/// preview can open the editor there (markdown-edit-here.js).
/// </summary>
/// <param name="text">The text the blocks were parsed from.</param>
internal sealed class SourcePositions(string text)
{
    public const string LineAttribute = "data-source-line", ColumnAttribute = "data-source-column";

    // The editors count line breaks like this too (\r\n, \r and \n alike), so the position is the same in both
    private readonly List<int> lineStarts = LineStarts(text);

    /// <summary>Blocks the parser made up itself (such as the list of footnotes) have no place in the text and get nothing.</summary>
    public void Mark(Block block)
    {
        if (block.Span.IsEmpty || block.Span.Start < 0)
            return;

        int line = lineStarts.BinarySearch(block.Span.Start);
        if (line < 0)
            line = ~line - 1;
        Position position = new(line + 1, block.Span.Start - lineStarts[line] + 1);

        // The highlighting leaves out the attributes of a code block, so it gets them on an element around it instead (CodeBlockExtension)
        if (block is CodeBlock)
            block.SetData(typeof(Position), position);
        else
            position.AddTo(block.GetAttributes());
    }

    private static List<int> LineStarts(string text)
    {
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            if (text[i] is '\r' or '\n')
                starts.Add(i + 1);
        }
        return starts;
    }

    private sealed record Position(int Line, int Column)
    {
        public void AddTo(HtmlAttributes attributes)
        {
            attributes.AddProperty(LineAttribute, Line.ToString(CultureInfo.InvariantCulture));
            attributes.AddProperty(ColumnAttribute, Column.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Puts a code block whose place in the text is known into a <c>div</c> that says where that is.</summary>
    public sealed class CodeBlockExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is not HtmlRenderer htmlRenderer)
                return;
            ObjectRendererCollection renderers = htmlRenderer.ObjectRenderers;
            int index = renderers.FindIndex(candidate => candidate.Accept(htmlRenderer, typeof(FencedCodeBlock)));
            if (index >= 0)
                renderers[index] = new PositionedCodeBlockRenderer(renderers[index]);
        }
    }

    private sealed class PositionedCodeBlockRenderer(IMarkdownObjectRenderer codeBlockRenderer) : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            if (block.GetData(typeof(Position)) is not Position position)
            {
                codeBlockRenderer.Write(renderer, block);
                return;
            }

            HtmlAttributes attributes = new();
            position.AddTo(attributes);
            renderer.Write("<div").WriteAttributes(attributes).Write('>');
            codeBlockRenderer.Write(renderer, block);
            renderer.EnsureLine().WriteLine("</div>");
        }
    }
}
