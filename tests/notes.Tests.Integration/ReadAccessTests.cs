using System.Net;
using System.Text.RegularExpressions;

namespace notes.Tests.Integration;

/// <summary>Opening entries, listings, raw bytes and downloads (security specification, Operations: the GET rows).</summary>
public sealed class ReadAccessTests(NotesAppFactory app) : IntegrationTest(app)
{
    [Theory]
    [InlineData("vault")]
    [InlineData("mixed/closed")]
    [InlineData("mixed/locked.md")]
    [InlineData("broken")]
    public async Task Known_entries_alice_may_not_read_refuse_to_open(string path)
    {
        foreach (string query in new[] { "", "?raw", "?download" })
        {
            HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/" + path + query);
            await AssertStatus(HttpStatusCode.Forbidden, response);
            Assert.DoesNotContain("contents", await BodyOf(response));
        }
    }

    [Fact]
    public async Task Known_directories_others_may_not_read_refuse_to_open()
        => await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Bob, "/team"));

    [Fact]
    public async Task Anonymous_users_are_not_shown_what_they_may_not_read()
    {
        string root = await BodyOf(await Client.GetAsync(Users.Anonymous, "/"));
        Assert.Contains("href=\"/docs\"", root);
        foreach (string name in new[] { "team", "vault", "broken" })
            Assert.DoesNotContain("> " + name + " ", root);
        Assert.DoesNotContain("data-locked-entry", root);

        string mixed = await BodyOf(await Client.GetAsync(Users.Anonymous, "/mixed"));
        Assert.Contains("href=\"/mixed/open.md\"", mixed);
        Assert.DoesNotContain("closed", mixed);
        Assert.DoesNotContain("locked.md", mixed);
        Assert.DoesNotContain("data-locked-entry", mixed);
    }

    [Fact]
    public async Task Without_read_on_the_root_the_root_page_shows_nothing_of_the_notes()
    {
        // Nobody is logged in, so the a of the base tree lets them read the root: take it away
        Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));
        HttpResponseMessage response = await Client.GetAsync(Users.Nobody, "/");
        await AssertStatus(HttpStatusCode.OK, response);
        string body = await BodyOf(response);

        Assert.Contains("id=\"root-unreadable\"", body);
        foreach (string name in new[] { "readme.md", "docs", "team", "vault", "mixed" })
            Assert.DoesNotContain(">" + name, body);

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Nobody, "/?download"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Nobody, "/?raw"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Nobody, "/?selection"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Nobody, "/?config"));
    }

    [Fact]
    public async Task Anonymous_users_without_read_on_the_root_get_the_login_link_only()
    {
        Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));

        string body = await BodyOf(await Client.GetAsync(Users.Anonymous, "/"));

        Assert.Contains("/_/Login", body);
        Assert.DoesNotContain("readme.md", body);
    }

    [Fact]
    public async Task Anonymous_users_who_may_read_are_told_they_may_see_more_after_logging_in()
    {
        string body = await BodyOf(await Client.GetAsync(Users.Anonymous, "/docs"));

        Assert.Contains("id=\"login-notice\"", body);
        Assert.Contains("a.md", body);
    }

    [Fact]
    public async Task A_listing_shows_unreadable_entries_by_name_and_kind_only()
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/mixed");
        await AssertStatus(HttpStatusCode.OK, response);
        string body = await BodyOf(response);

        // Listed, locked, not linked, not selectable
        // Each with the reason as its tooltip, and a box that can't be ticked
        Assert.Matches(@"<tr data-locked-entry=""closed"" title=""[^""]+"">\s*<td[^>]*><input[^>]*disabled", body);
        Assert.Matches(@"<tr data-locked-entry=""locked.md"" title=""[^""]+"">\s*<td[^>]*><input[^>]*disabled", body);
        Assert.DoesNotContain("href=\"/mixed/closed\"", body);
        Assert.DoesNotContain("href=\"/mixed/locked.md\"", body);
        Assert.DoesNotContain("data-path=\"mixed/closed\"", body);
        Assert.DoesNotContain("data-path=\"mixed/locked.md\"", body);
        // The readable ones are links
        Assert.Contains("href=\"/mixed/open.md\"", body);
        Assert.Contains("data-path=\"mixed/open.md\"", body);
        // Never the configuration
        Assert.DoesNotContain(".config.json", body);
    }

    [Fact]
    public async Task Bob_sees_the_names_of_team_and_vault_but_nothing_in_them()
    {
        string body = await BodyOf(await Client.GetAsync(Users.Bob, "/"));

        Assert.Contains("team", body);
        Assert.Contains("vault", body);
        Assert.DoesNotContain("href=\"/team\"", body);
        Assert.DoesNotContain("plan.md", body);
    }

    [Fact]
    public async Task The_reserved_folder_is_never_listed()
    {
        _ = Tree.File("_/temp/user/x/y.part");

        foreach (TestUser user in new[] { Users.Alice, Users.Admin })
        {
            string body = await BodyOf(await Client.GetAsync(user, "/"));
            Assert.DoesNotMatch(new Regex(@"href=""/_"""), body);
            Assert.DoesNotContain("data-path=\"_\"", body);
        }
    }

    [Fact]
    public async Task Files_open_in_the_editor_read_only_without_write()
    {
        // (Markdown that can't be changed shows its source read-only at ?edit, see MarkdownPreviewTests)
        Tree.Config("readonly", Cfg.Folder(Cfg.Perms(rwReadOnly: ["g:team"])));
        _ = Tree.File("readonly/view.txt", "text");
        string view = await BodyOf(await Client.GetAsync(Users.Alice, "/readonly/view.txt"));
        string open = await BodyOf(await Client.GetAsync(Users.Alice, "/mixed/open.md?edit"));

        Assert.Contains("\"canWrite\":false", view);
        Assert.Contains("\"canWrite\":true", open);
    }

    [Fact]
    public async Task Bob_has_no_buttons_that_change_things()
    {
        string body = await BodyOf(await Client.GetAsync(Users.Bob, "/docs/a.md"));

        Assert.DoesNotContain("id=\"save-button\"", body);
        Assert.DoesNotContain("id=\"delete-button\"", body);
        Assert.Contains("id=\"download-button\"", body);
    }

    [Fact]
    public async Task Only_admins_have_the_way_to_the_permissions()
    {
        Assert.DoesNotContain("?config", await BodyOf(await Client.GetAsync(Users.Alice, "/docs")));
        Assert.Contains("?config", await BodyOf(await Client.GetAsync(Users.Admin, "/docs")));
    }

    [Fact]
    public async Task A_file_downloads_as_an_attachment()
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/docs/a.md?download");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("a", await BodyOf(response));
    }

    [Fact]
    public async Task A_zip_leaves_out_what_alice_may_not_read_and_the_configurations()
    {
        _ = Tree.File("_/temp/x");

        List<string> names = await ZipNamesAsync(await Client.GetAsync(Users.Alice, "/?download"));

        Assert.Contains("readme.md", names);
        Assert.Contains("docs/nested/b.md", names);
        Assert.Contains("mixed/open.md", names);
        Assert.Contains("team/plan.md", names);
        Assert.DoesNotContain(names, name => name.StartsWith("vault", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("broken", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("mixed/closed", StringComparison.Ordinal));
        Assert.DoesNotContain("mixed/locked.md", names);
        Assert.DoesNotContain(names, name => name.Contains(".config.json", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_admins_zip_has_everything_including_the_configurations()
    {
        List<string> names = await ZipNamesAsync(await Client.GetAsync(Users.Admin, "/mixed?download"));

        Assert.Contains(".config.json", names);
        Assert.Contains("closed/.config.json", names);
        Assert.Contains("closed/hidden.md", names);
        Assert.Contains("locked.md", names);
    }

    [Fact]
    public async Task Raw_bytes_only_for_media_and_never_sniffed()
    {
        _ = Tree.File("docs/pic.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]);
        _ = Tree.File("docs/page.html", "<script>alert(1)</script>");
        _ = Tree.File("docs/drawing.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        HttpResponseMessage png = await Client.GetAsync(Users.Alice, "/docs/pic.png?raw");
        await AssertStatus(HttpStatusCode.OK, png);
        Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", png.Headers.GetValues("X-Content-Type-Options").Single());

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/page.html?raw"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/drawing.svg?raw"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs?raw"));
    }

    [Fact]
    public async Task Media_files_have_a_button_to_their_raw_bytes_and_nothing_else_does()
    {
        _ = Tree.File("docs/pic.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]);
        _ = Tree.File("docs/blob.bin", [0, 1, 2, 3]);

        string png = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/pic.png"));
        Assert.Matches(@"<a id=""raw-button"" href=""\?raw""", png);


        // Binary, but there are no raw bytes of it (?raw is a 404)
        foreach (string path in new[] { "/docs/blob.bin", "/docs/a.md", "/docs" })
            Assert.DoesNotContain("id=\"raw-button\"", await BodyOf(await Client.GetAsync(Users.Alice, path)));
    }

    [Fact]
    public async Task Html_in_a_note_is_shown_as_text_and_can_not_break_out_of_the_page()
    {
        _ = Tree.File("docs/evil.md", "</script><script>window.pwned = 1</script><img src=x onerror=alert(1)>");

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/evil.md"));

        Assert.DoesNotContain("</script><script>window.pwned", body);
        Assert.DoesNotContain("<img src=x onerror", body);
    }

    [Fact]
    public async Task Names_are_encoded_in_listings()
    {
        string name = OperatingSystem.IsWindows() ? "a'b&c onmouseover=alert(1).md" : "\"><img src=x onerror=alert(1)>.md";
        _ = Tree.File("docs/" + name);

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));

        Assert.DoesNotContain(name, body);
        Assert.DoesNotContain("<img src=x", body);
    }

    [Fact]
    public async Task Paths_are_found_whatever_their_case_but_listed_as_they_are()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/DOCS/A.MD"));
        Assert.Contains("data-path=\"docs/a.md\"", await BodyOf(await Client.GetAsync(Users.Alice, "/Docs")));
    }

    [Fact]
    public async Task Errors_do_not_show_server_internals()
    {
        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/does-not-exist"));

        Assert.DoesNotContain(App.NotesDir, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body);
    }
}
