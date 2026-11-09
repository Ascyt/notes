using System.Net;
using System.Text.RegularExpressions;

namespace notes.Tests.Integration;

/// <summary>Markdown files are shown rendered, and only edited when asked for (<c>?edit</c>) by someone who may change them.</summary>
public sealed class MarkdownPreviewTests(NotesAppFactory app) : IntegrationTest(app)
{
    [Fact]
    public async Task A_markdown_file_opens_as_the_rendered_preview_with_a_way_to_edit()
    {
        Tree.File("docs/note.md", "# Title\n\nSome **bold** text.");

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/note.md"));

        Assert.Contains("<h1 id=\"user-content-title\">Title</h1>", body);
        Assert.Contains("<strong>bold</strong>", body);
        Assert.DoesNotContain("id=\"editor\"", body);
        Assert.DoesNotContain("id=\"save-button\"", body);
        Assert.DoesNotContain("id=\"preview-button\"", body);
        Assert.Matches(@"<a id=""edit-button"" href=""\?edit""[^>]* key=""e""", body);
    }

    [Fact]
    public async Task The_preview_has_its_own_stylesheet_which_the_editor_and_other_pages_do_not_load()
    {
        // (The static assets are served under a name with their fingerprint in it)
        string preview = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md"));
        Match link = Regex.Match(preview, @"<link rel=""stylesheet"" href=""(/css/markdown[^""]*\.css)""");
        Assert.True(link.Success, "the preview links the stylesheet");
        Assert.DoesNotMatch(@"css/markdown", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md?edit")));
        Assert.DoesNotMatch(@"css/markdown", await BodyOf(await Client.GetAsync(Users.Alice, "/docs")));

        HttpResponseMessage css = await Client.GetAsync(Users.Alice, link.Groups[1].Value);
        await AssertStatus(HttpStatusCode.OK, css);
        Assert.Contains(".markdown-body", await BodyOf(css));
        string site = Regex.Match(preview, @"href=""(/css/site[^""]*\.css)""").Groups[1].Value;
        Assert.DoesNotContain(".markdown-body", await BodyOf(await Client.GetAsync(Users.Alice, site)));
    }

    [Fact]
    public async Task The_editor_of_a_markdown_file_is_at_edit_and_leads_back_to_the_preview()
    {
        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md?edit"));

        Assert.Contains("id=\"editor\"", body);
        Assert.Contains("id=\"save-button\"", body);
        Assert.Contains("const canWrite = true;", body);
        Assert.Matches(@"<a id=""preview-button"" href=""\?""[^>]* key=""p""", body);
        Assert.DoesNotContain("id=\"edit-button\"", body);
        Assert.DoesNotContain("id=\"markdown-preview\"", body);
    }

    [Fact]
    public async Task Whoever_may_not_change_a_markdown_file_only_gets_the_preview()
    {
        // Bob may read docs but not write, mixed/view.md is read-only for alice's team
        foreach ((TestUser user, string path) in new[] { (Users.Bob, "/docs/a.md"), (Users.Alice, "/mixed/view.md") })
        {
            foreach (string query in new[] { "", "?edit" })
            {
                string body = await BodyOf(await Client.GetAsync(user, path + query));

                Assert.Contains("id=\"markdown-preview\"", body);
                Assert.DoesNotContain("id=\"editor\"", body);
                Assert.DoesNotContain("id=\"save-button\"", body);
                Assert.DoesNotContain("id=\"edit-button\"", body);
                Assert.DoesNotContain("id=\"preview-button\"", body);
                Assert.DoesNotContain("createEditor(", body);
            }
        }
    }

    [Fact]
    public async Task Other_text_files_only_have_the_editor()
    {
        Tree.File("docs/data.json", "{ \"a\": 1 }");
        Tree.File("docs/plain.txt", "# not markdown");

        foreach (string path in new[] { "/docs/data.json", "/docs/plain.txt" })
        {
            foreach (string query in new[] { "", "?edit" })
            {
                string body = await BodyOf(await Client.GetAsync(Users.Alice, path + query));

                Assert.Contains("id=\"editor\"", body);
                Assert.DoesNotContain("markdown-body", body);
                Assert.DoesNotContain("id=\"edit-button\"", body);
                Assert.DoesNotContain("id=\"preview-button\"", body);
            }
        }
    }

    [Fact]
    public async Task Html_in_markdown_is_shown_as_text_and_links_that_run_code_lead_nowhere()
    {
        Tree.File("docs/evil.md", string.Join("\n\n",
            "<script>window.pwned = 1</script>",
            "<img src=x onerror=alert(1)>",
            "[click](javascript:alert(1)) and [tab](java\tscript:alert(1)) and [data](data:text/html,<script>alert(1)</script>)",
            "<javascript:alert(1)>",
            "![pic](javascript:alert(1))",
            "# Heading {onmouseover=alert(1) .modal #editor}"));

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/evil.md"));

        Assert.DoesNotContain("<script>window.pwned", body);
        Assert.DoesNotContain("<img src=x", body);
        // Nothing is linked to or loaded from them (what is left of them is shown as text), and the attributes did not make it
        string article = body[body.IndexOf("<article", StringComparison.Ordinal)..body.IndexOf("</article>", StringComparison.Ordinal)];
        Assert.DoesNotMatch(@"(href|src)=""[^""]", article);
        Assert.DoesNotContain("<h1 onmouseover", article);
        Assert.DoesNotContain("class=\"modal\"", article);
        Assert.DoesNotContain("id=\"editor\"", body);
    }

    [Fact]
    public async Task An_empty_markdown_file_says_so()
    {
        Tree.File("docs/empty.md", "");

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/empty.md"));

        Assert.Contains("id=\"empty-file\"", body);
    }

    [Fact]
    public async Task Markdown_that_can_not_be_read_is_not_rendered()
    {
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Alice, "/mixed/locked.md"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Alice, "/mixed/locked.md?edit"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/secret.md?edit"));
    }
}
