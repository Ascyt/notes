using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace notes.Tests.Integration;

/// <summary>Selecting, and seeing and changing the permissions (security specification, Operations).</summary>
public sealed class SelectionAndConfigTests(NotesAppFactory app) : IntegrationTest(app)
{
    // ---- Selection

    [Fact]
    public async Task Only_openable_entries_can_be_selected_the_rest_is_skipped_silently()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice,
            "docs/a.md", "DOCS/NESTED", "/mixed/open.md/",
            "vault", "vault/secret.md", "mixed/locked.md", "mixed/closed", "mixed/closed/hidden.md", "broken/x.md",
            "", "/", ".", "..", "../outside", "_", "_/temp", ".config.json", "mixed/.config.json", "missing.md", "docs/../vault/secret.md");

        Assert.Equal(["docs/a.md", "docs/nested", "mixed/open.md"], selection.Paths);
        Assert.Equal(1, selection.DirectoryCount);
    }

    [Fact]
    public async Task What_the_card_says_about_deleting_comes_from_the_permissions()
    {
        Assert.True((await SelectAsync(Users.Alice, "docs/a.md")).CanDelete);
        // Only the entry itself: what is inside a directory (mixed/keep.md may not be deleted) is up to the transfer, which refuses it
        Assert.True((await SelectAsync(Users.Alice, "mixed")).CanDelete);
        // (The selection is of all that is selected: one entry that can't be deleted is enough)
        Assert.False((await SelectAsync(Users.Alice, "shelf/item.md")).CanDelete);
    }

    [Fact]
    public async Task Users_without_an_id_or_without_read_on_the_root_have_no_selection()
    {
        // Nobody is logged in, so the a of the base tree lets them read the root: take it away
        Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/?selection"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostJsonAsync(Users.Anonymous, "/?selection=add", new { paths = new[] { "docs/a.md" } }));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Nobody, "/?selection"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostJsonAsync(Users.Nobody, "/?selection=add", new { paths = new[] { "docs/a.md" } }));
    }

    [Fact]
    public async Task Selections_are_per_user()
    {
        _ = await SelectAsync(Users.Alice, "docs/a.md");

        Assert.Empty((await GetSelectionAsync(Users.Carol)).Paths);
        Assert.Equal(["docs/a.md"], (await GetSelectionAsync(Users.Alice)).Paths);
    }

    [Fact]
    public async Task Malformed_selection_requests_are_refused()
    {
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice, "/?selection=add", new { paths = Enumerable.Repeat("docs/a.md", 10_001).ToArray() }));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice, "/?selection=add", new { other = 1 }));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice, "/?selection=steal", new { paths = Array.Empty<string>() }));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.SendAsync(Users.Alice, HttpMethod.Post, "/?selection=add", new StringContent("{", System.Text.Encoding.UTF8, "application/json")));
    }

    [Fact]
    public async Task Unselecting_needs_nothing()
    {
        _ = await SelectAsync(Users.Alice, "team/plan.md");
        Tree.Config("team", Cfg.Folder(Cfg.Perms(inherit: false)));

        HttpResponseMessage response = await Client.PostJsonAsync(Users.Alice, "/?selection=remove", new { paths = new[] { "team/plan.md" } });

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Empty((await GetSelectionAsync(Users.Alice)).Paths);
    }

    // ---- The permissions themselves (?config)

    [Theory]
    [InlineData("")]
    [InlineData("docs")]
    [InlineData("docs/a.md")]
    [InlineData("vault")]
    public async Task Only_admins_see_permissions_everyone_else_gets_a_missing_page(string path)
    {
        foreach (TestUser user in new[] { Users.Alice, Users.Bob, Users.Anonymous, Users.Carol })
        {
            HttpResponseMessage response = await Client.GetAsync(user, "/" + path + "?config");
            await AssertStatus(HttpStatusCode.NotFound, response);
            Assert.DoesNotContain("perms", await BodyOf(response));
        }

        HttpResponseMessage admin = await Client.GetAsync(Users.Admin, "/" + path + "?config");
        await AssertStatus(HttpStatusCode.OK, admin);
        Assert.Contains("inherit", await BodyOf(admin));
    }

    [Theory]
    [InlineData("")]
    [InlineData("docs")]
    [InlineData("docs/a.md")]
    public async Task Only_admins_change_permissions(string path)
    {
        string before = Tree.Snapshot();
        string grant = path.EndsWith(".md")
            ? """{ "perms": { "inherit": false, "rwAllow": ["g:guests"], "cdAllow": ["g:guests"] } }"""
            : """{ "perms": { "inherit": false, "rwAllow": ["g:guests"], "cdAllow": ["g:guests"] }, "files": [] }""";

        foreach (TestUser user in new[] { Users.Alice, Users.Bob, Users.Anonymous })
        {
            HttpResponseMessage response = await Client.PostAsync(user, "/" + path + "?config", grant);
            await AssertStatus(HttpStatusCode.NotFound, response);
            // Exactly like a path that doesn't exist
            HttpResponseMessage missing = await Client.PostAsync(user, "/does-not-exist.md?config", grant);
            Assert.Equal(await BodyOf(missing), await BodyOf(response));
        }

        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task An_admin_saves_the_text_as_written_with_entries_for_the_files_that_lack_one()
    {
        const string text = """
            {
                "version": "1.0.0",
                // only carol
                "perms": { "inherit": false, "rwAllow": ["u:carol"], "cdAllow": ["u:carol"] },
                "files": [
                ]
            }
            """;

        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/docs?config", text);

        await AssertStatus(HttpStatusCode.OK, response);
        string saved = Tree.ReadConfig("docs")!;
        Assert.Equal(saved, await BodyOf(response));
        Assert.Contains("// only carol", saved);
        Assert.Contains("\"a.md\"", saved);
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Alice, "/docs"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Carol, "/docs"));
    }

    [Fact]
    public async Task Saving_a_list_of_files_with_only_a_comment_in_it_keeps_the_folder_open()
    {
        const string text = """
            {
                "version": "1.0.0",
                "perms": { "inherit": true },
                "files": [ // filled in by the app
                ]
            }
            """;

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs?config", text));

        Assert.Contains("// filled in by the app", Tree.ReadConfig("docs"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs"));
        // ...and so does the next file that comes in
        await AssertStatus(HttpStatusCode.Redirect, await Client.PostAsync(Users.Alice, "/docs?create=file"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs"));
    }

    [Fact]
    public async Task An_admin_changes_the_permissions_of_one_file()
    {
        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/mixed/open.md?config", """{ "perms": { "inherit": false } }""");

        await AssertStatus(HttpStatusCode.OK, response);
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Alice, "/mixed/open.md"));
        // The rest of the folder's configuration is still there
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/mixed/view.md", "x"));
    }

    [Theory]
    [InlineData("""{ "version": "1.0.0", "perms": { "inherit": true, "rwAllow": ["a"] }, "files": [] }""")]
    [InlineData("""{ "version": "1.0.0", "perms": { "inherit": true, "cdCreateOnly": ["a"] }, "files": [] }""")]
    [InlineData("""{ "version": "1.0.0", "perms": { "inherit": true }, "files": [ { "name": "missing.md", "perms": { "inherit": true } } ] }""")]
    [InlineData("""{ "version": "1.0.0", "perms": { "inherit": true } }""")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [] }""")] // no version
    [InlineData("""{ "version": "2.0.0", "perms": { "inherit": true }, "files": [] }""")] // from a newer version of the app
    [InlineData("""{ "version": "1", "perms": { "inherit": true }, "files": [] }""")]
    [InlineData("""{ "version": 1, "perms": { "inherit": true }, "files": [] }""")]
    [InlineData("{")]
    [InlineData("")]
    public async Task Invalid_permissions_change_nothing(string text)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Admin, "/docs?config", text));

        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task A_configuration_of_an_older_version_is_saved_as_the_current_one()
    {
        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/docs?config", """{ "version": "0.5.0", "perms": { "inherit": true }, "files": [] }""");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains("\"version\": \"1.0.0\"", Tree.ReadConfig("docs"));
    }

    [Fact]
    public async Task The_configuration_the_editor_shows_has_the_current_version_even_if_the_file_has_none()
    {
        Tree.Config("legacy", """{ "perms": { "inherit": true }, "files": [] }""");

        // A page, in which the text of the editor is a string of the script
        string page = await BodyOf(await Client.GetAsync(Users.Admin, "/legacy?config"));
        Match contents = Regex.Match(page, @"value: (""(?:[^""\\]|\\.)*""),");
        Assert.True(contents.Success, page);

        Assert.Contains("\"version\": \"1.0.0\"", JsonSerializer.Deserialize<string>(contents.Groups[1].Value));
    }

    [Fact]
    public async Task The_permissions_of_a_file_can_not_name_another_file()
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Admin, "/mixed/open.md?config", """{ "name": "locked.md", "perms": { "inherit": true, "rwAllow": ["g:team"] } }"""));

        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task The_permissions_of_a_file_in_a_folder_with_an_invalid_configuration_can_not_be_saved()
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.Conflict, await Client.PostAsync(Users.Admin, "/broken/x.md?config", """{ "perms": { "inherit": true } }"""));

        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task An_invalid_saved_configuration_is_pointed_out_to_admins()
        => Assert.Contains("id=\"config-warning\"", await BodyOf(await Client.GetAsync(Users.Admin, "/broken?config")));

    [Fact]
    public async Task Too_long_permissions_are_refused()
        => await AssertStatus(HttpStatusCode.RequestEntityTooLarge, await Client.PostAsync(Users.Admin, "/docs?config", new string(' ', 1024 * 1024 + 1)));

    [Fact]
    public async Task Configuration_files_can_not_be_reached_by_any_other_way()
    {
        foreach (TestUser user in new[] { Users.Admin, Users.Alice })
        {
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(user, "/mixed/.config.json"));
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(user, "/mixed/.config.json?raw"));
            await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(user, "/mixed/.config.json", "{}"));
            await AssertStatus(HttpStatusCode.NotFound, await Client.DeleteAsync(user, "/mixed/.config.json"));
        }

        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "mixed/.config.json");
        Assert.Empty(selection.Paths);
        Assert.NotNull(Tree.ReadConfig("mixed"));
    }
}
