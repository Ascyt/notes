using notes.Features.View.Editor;

namespace notes.Tests.Unit;

/// <summary>What the preview of a markdown file is made of, and what it must never contain.</summary>
public sealed class MarkdownRendererTests
{
    [Fact]
    public void Markdown_becomes_html()
    {
        string html = MarkdownRenderer.ToHtml("Some *text* with `code`.\n\n- one\n- two");

        Assert.Contains("<em>text</em>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<li>one</li>", html);
    }

    [Fact]
    public void The_extensions_of_the_cheat_sheet_work()
    {
        string html = MarkdownRenderer.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |\n\n~~gone~~\n\n- [x] done\n- [ ] todo\n\nNote[^1]\n\n[^1]: The note.\n\n:smile:");

        Assert.Contains("<table>", html);
        Assert.Contains("<del>gone</del>", html);
        Assert.Contains("class=\"contains-task-list\"", html);
        Assert.Contains("class=\"task-list-item\"", html);
        Assert.Contains("class=\"footnotes\"", html);
        Assert.Contains("😄", html);
    }

    [Fact]
    public void Raw_html_is_shown_as_text()
    {
        string html = MarkdownRenderer.ToHtml("<script>alert(1)</script>\n\n<b onclick=x>bold</b>");

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<b onclick", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JaVaScRiPt:alert(1))")]
    [InlineData("[x](java\tscript:alert(1))")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[x](vbscript:msgbox)")]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("![x](data:image/svg+xml,<svg>)")]
    [InlineData("![x](mailto:a@b.c)")]
    public void Addresses_that_can_run_code_are_dropped(string markdown)
    {
        string html = MarkdownRenderer.ToHtml(markdown);

        // Whatever of it is shown as text is harmless; what counts is that nothing is linked to or loaded from it
        Assert.DoesNotMatch(@"(href|src)=""[^""]", html);
    }

    [Theory]
    [InlineData("[x](https://example.com/a?b=c)", "href=\"https://example.com/a?b=c\"")]
    [InlineData("[x](http://example.com)", "href=\"http://example.com\"")]
    [InlineData("[x](mailto:a@b.c)", "href=\"mailto:a@b.c\"")]
    [InlineData("[x](other.md)", "href=\"other.md\"")]
    [InlineData("[x](/docs/other.md)", "href=\"/docs/other.md\"")]
    [InlineData("[x](../up/other.md?edit)", "href=\"../up/other.md?edit\"")]
    [InlineData("<https://example.com>", "href=\"https://example.com\"")]
    public void Addresses_that_lead_somewhere_harmless_are_kept(string markdown, string expected)
        => Assert.Contains(expected, MarkdownRenderer.ToHtml(markdown));

    [Theory]
    [InlineData("![x](pic.png)", "src=\"pic.png?raw\"")]
    [InlineData("![x](../img/pic.png)", "src=\"../img/pic.png?raw\"")]
    [InlineData("![x](/docs/pic.png)", "src=\"/docs/pic.png?raw\"")]
    [InlineData("![x](https://example.com/pic.png)", "src=\"https://example.com/pic.png\"")]
    public void Images_of_the_notes_are_their_raw_bytes(string markdown, string expected)
        => Assert.Contains(expected, MarkdownRenderer.ToHtml(markdown));

    [Fact]
    public void Ids_can_not_be_those_of_the_page_and_links_to_them_follow()
    {
        string html = MarkdownRenderer.ToHtml("# Editor\n\n[up](#editor)\n\n## Other {#save-button}");

        Assert.Contains("id=\"user-content-editor\"", html);
        Assert.Contains("href=\"#user-content-editor\"", html);
        Assert.Contains("id=\"user-content-save-button\"", html);
        Assert.DoesNotContain("id=\"editor\"", html);
        Assert.DoesNotContain("id=\"save-button\"", html);
    }

    [Fact]
    public void Other_attributes_and_classes_are_dropped()
    {
        string html = MarkdownRenderer.ToHtml("# Head {.modal onclick=alert(1) style=\"position:fixed\"}\n\n[x](a.md){onmouseover=alert(1) .btn}");

        Assert.DoesNotContain("modal", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("style=", html);
        Assert.DoesNotContain("onmouseover", html);
        Assert.DoesNotContain("btn", html);
    }

    [Fact]
    public void A_fenced_code_block_keeps_its_language()
        => Assert.Contains("class=\"language-csharp\"", MarkdownRenderer.ToHtml("```csharp\nvar x = 1;\n```"));
}
