using System.Globalization;
using Markdig.Syntax;

namespace notes.Pages.Editor.Markdown;

/// <summary>
/// Lists as notes are written: a blank line between two items ends the list and starts another one right after it, instead of making the
/// whole list loose (every item a paragraph, all equally far apart) as CommonMark says. So that
/// <code>
/// - a
/// - b
///
/// - c
/// </code>
/// shows a and b close together, then a gap, then c. A list is only loose (paragraphs) if one of its items holds a blank line itself.
/// </summary>
internal static class MarkdownLists
{
    /// <param name="markdown">The text the document was parsed from, where the blank lines are.</param>
    public static void SplitAtBlankLines(MarkdownDocument document, string markdown)
    {
        // (A list taken apart here gets new lists next to it, which need nothing more)
        foreach (ListBlock list in document.Descendants<ListBlock>().ToList())
            Split(list, markdown);
    }

    private static void Split(ListBlock list, string markdown)
    {
        ContainerBlock parent = list.Parent!;
        ListBlock current = list;
        // The items after a blank line move into a list of their own, right after the one before
        for (int i = 1; i < current.Count; i++)
        {
            if (!HasBlankLineBetween(markdown, current[i - 1], current[i]))
                continue;

            ListBlock next = NewListLike(current, (ListItemBlock)current[i]);
            while (current.Count > i)
            {
                Block item = current[i];
                current.RemoveAt(i);
                next.Add(item);
            }

            current.Span = new SourceSpan(current.Span.Start, current[^1].Span.End);
            next.Span = new SourceSpan(next[0].Span.Start, next[^1].Span.End);
            parent.Insert(parent.IndexOf(current) + 1, next);
            current.IsLoose = HasLooseItem(current, markdown);
            current = next;
            i = 0;
        }

        current.IsLoose = HasLooseItem(current, markdown);
    }

    /// <summary>An empty list of the same kind, whose numbers go on from the item it starts with.</summary>
    private static ListBlock NewListLike(ListBlock list, ListItemBlock first)
        => new(list.Parser)
        {
            BulletType = list.BulletType,
            IsOrdered = list.IsOrdered,
            OrderedDelimiter = list.OrderedDelimiter,
            DefaultOrderedStart = list.DefaultOrderedStart,
            OrderedStart = list.IsOrdered ? first.Order.ToString(CultureInfo.InvariantCulture) : null,
            Line = first.Line,
            Column = first.Column
        };

    /// <summary>Whether an item holds a blank line between two of its blocks (e.g. two paragraphs), which makes the list loose.</summary>
    private static bool HasLooseItem(ListBlock list, string markdown)
        => list.OfType<ListItemBlock>().Any(item => Enumerable.Range(1, Math.Max(0, item.Count - 1))
            .Any(i => HasBlankLineBetween(markdown, item[i - 1], item[i])));

    /// <summary>Whether a line with nothing but spaces lies between the end of what is written in one block and the start of the next.</summary>
    private static bool HasBlankLineBetween(string markdown, Block before, Block after)
    {
        int from = Math.Clamp(EndOfContent(before) + 1, 0, markdown.Length);
        int to = Math.Clamp(after.Span.Start, from, markdown.Length);

        // A blank line is one that starts after a line break and ends at the next, with only whitespace (or \r) in between
        bool afterLineBreak = false, onlyWhitespace = true;
        for (int i = from; i < to; i++)
        {
            char character = markdown[i];
            if (character == '\n')
            {
                if (afterLineBreak && onlyWhitespace)
                    return true;
                afterLineBreak = true;
                onlyWhitespace = true;
            }
            else if (!char.IsWhiteSpace(character))
                onlyWhitespace = false;
        }

        return false;
    }

    /// <summary>
    /// Where what is written in the block ends: a list item (or any container) also takes in the blank lines after it, as more of it could
    /// follow them, so it ends where its last block does.
    /// </summary>
    private static int EndOfContent(Block block)
        => block is ContainerBlock { Count: > 0 } container ? EndOfContent(container[^1]) : block.Span.End;
}
