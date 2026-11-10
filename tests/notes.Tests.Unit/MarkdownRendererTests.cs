using notes.Features.View.Editor;

namespace notes.Tests.Unit;

/// <summary>What the preview of a markdown file is made of, and what it must never contain.</summary>
public sealed class MarkdownRendererTests
{
    private const string SiteHost = "notes.example";

    private static string Render(string markdown) => MarkdownRenderer.Render(markdown, SiteHost).Html;

    [Fact]
    public void Markdown_becomes_html()
    {
        string html = Render("Some *text* with `code`.\n\n- one\n- two");

        Assert.Contains("<em>text</em>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<li>one</li>", html);
    }

    [Fact]
    public void The_extensions_of_the_cheat_sheet_work()
    {
        string html = Render("| a | b |\n|---|---|\n| 1 | 2 |\n\n~~gone~~\n\n- [x] done\n- [ ] todo\n\nNote[^1]\n\n[^1]: The note.\n\n:smile:");

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
        string html = Render("<script>alert(1)</script>\n\n<b onclick=x>bold</b>");

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
        string html = Render(markdown);

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
        => Assert.Contains(expected, Render(markdown));

    [Theory]
    [InlineData("![x](pic.png)", "src=\"pic.png?raw\"")]
    [InlineData("![x](../img/pic.png)", "src=\"../img/pic.png?raw\"")]
    [InlineData("![x](/docs/pic.png)", "src=\"/docs/pic.png?raw\"")]
    [InlineData("![x](https://example.com/pic.png)", "src=\"https://example.com/pic.png\"")]
    public void Images_of_the_notes_are_their_raw_bytes(string markdown, string expected)
        => Assert.Contains(expected, Render(markdown));

    [Fact]
    public void Ids_can_not_be_those_of_the_page_and_links_to_them_follow()
    {
        string html = Render("# Editor\n\n[up](#editor)\n\n## Other {#save-button}");

        Assert.Contains("id=\"user-content-editor\"", html);
        Assert.Contains("href=\"#user-content-editor\"", html);
        Assert.Contains("id=\"user-content-save-button\"", html);
        Assert.DoesNotContain("id=\"editor\"", html);
        Assert.DoesNotContain("id=\"save-button\"", html);
    }

    [Fact]
    public void Other_attributes_and_classes_are_dropped()
    {
        string html = Render("# Head {.modal onclick=alert(1) style=\"position:fixed\"}\n\n[x](a.md){onmouseover=alert(1) .btn}");

        Assert.DoesNotContain("modal", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("style=", html);
        Assert.DoesNotContain("onmouseover", html);
        Assert.DoesNotContain("btn", html);
    }

    [Fact]
    public void Code_of_a_known_language_is_highlighted_and_stays_escaped()
    {
        string html = Render("```csharp\nvar x = \"<script>\"; // note\n```");

        Assert.Contains("class=\"keyword\"", html);
        Assert.Contains("class=\"comment\"", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void A_code_block_of_an_unknown_language_keeps_its_language()
        => Assert.Contains("class=\"language-nosuchlanguage\"", Render("```nosuchlanguage\n<b>x</b>\n```"));

    [Fact]
    public void Math_is_left_for_the_browser_escaped_and_says_so()
    {
        RenderedMarkdown rendered = MarkdownRenderer.Render("Inline $a < b$.\n\n$$\n\\frac{1}{2} <script>\n$$", SiteHost);

        Assert.True(rendered.HasMath);
        Assert.Contains("<span class=\"math\">", rendered.Html);
        Assert.Contains("<div class=\"math\">", rendered.Html);
        Assert.DoesNotContain("<script", rendered.Html);
        Assert.False(MarkdownRenderer.Render("Costs 5 dollars.", SiteHost).HasMath);
    }

    [Fact]
    public void A_mermaid_block_stays_an_escaped_code_block_and_says_so()
    {
        RenderedMarkdown rendered = MarkdownRenderer.Render("```mermaid\ngraph TD; A-->B[\"<img src=x onerror=alert(1)>\"]\n```", SiteHost);

        Assert.True(rendered.HasDiagrams);
        Assert.Contains("class=\"language-mermaid\"", rendered.Html);
        Assert.DoesNotContain("<img", rendered.Html);
        Assert.False(MarkdownRenderer.Render("```js\nx\n```", SiteHost).HasDiagrams);
    }

    [Fact]
    public void Alerts_keep_their_kind()
    {
        string html = Render("> [!WARNING]\n> Careful.");

        Assert.Contains("markdown-alert", html);
        Assert.Contains("markdown-alert-warning", html);
    }

    [Fact]
    public void A_custom_container_gets_its_name_only_behind_a_prefix()
    {
        string html = Render(":::warning\nCareful.\n:::\n\n:::modal\nx\n:::");

        Assert.Contains("class=\"markdown-container markdown-container-warning\"", html);
        Assert.Contains("markdown-container-modal", html);
        Assert.DoesNotContain("\"modal\"", html);
        Assert.DoesNotContain(" modal\"", html);
    }

    [Fact]
    public void Figures_footers_and_citations_work()
    {
        string html = Render("^^^\n![pic](a.png)\n^^^ The caption\n\n^^ A footer\n\n\"\"A citation\"\"");

        Assert.Contains("<figcaption>The caption</figcaption>", html);
        Assert.Contains("<footer>A footer</footer>", html);
        Assert.Contains("<cite>A citation</cite>", html);
    }

    [Fact]
    public void Emoji_codes_become_emoji_but_smileys_stay_text()
    {
        string html = Render(":joy: :) ;-) :D");

        Assert.Contains("😂", html);
        Assert.Contains(":) ;-) :D", html);
    }

    [Fact]
    public void Abbreviations_are_marked_and_say_so()
    {
        RenderedMarkdown rendered = MarkdownRenderer.Render("The W3C.\n\n*[W3C]: World Wide Web Consortium", SiteHost);

        Assert.True(rendered.HasAbbreviations);
        Assert.Contains("<abbr title=\"World Wide Web Consortium\">W3C</abbr>", rendered.Html);
        Assert.False(MarkdownRenderer.Render("The W3C.", SiteHost).HasAbbreviations);
    }

    [Fact]
    public void A_line_break_in_the_text_is_one_in_the_preview()
        => Assert.Contains("one<br />", Render("one\ntwo"));

    [Theory]
    [InlineData("[x](https://example.com)")]
    [InlineData("[x](http://other.notes.example/a)")]
    [InlineData("[x](//example.com/a)")]
    [InlineData("<https://example.com>")]
    public void Links_to_other_sites_open_in_a_new_tab(string markdown)
    {
        string html = Render(markdown);

        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Theory]
    [InlineData("[x](other.md)")]
    [InlineData("[x](/docs/other.md)")]
    [InlineData("[x](#heading)")]
    [InlineData("[x](https://notes.example/docs)")]
    [InlineData("[x](https://NOTES.example:8443/docs)")]
    [InlineData("[x](mailto:a@b.c)")]
    [InlineData("![x](https://example.com/pic.png)")]
    public void Links_to_the_notes_stay_in_the_tab(string markdown)
        => Assert.DoesNotContain("target=", Render(markdown));

    [Fact]
    public void A_link_can_not_set_its_own_target()
        => Assert.DoesNotContain("target=", Render("[x](a.md){target=_blank}"));
}
