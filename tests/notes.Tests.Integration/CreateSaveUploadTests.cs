using System.Net;

namespace notes.Tests.Integration;

/// <summary>Creating, saving and uploading (security specification, Operations).</summary>
public sealed class CreateSaveUploadTests(NotesAppFactory app) : IntegrationTest(app)
{
    // ---- Create

    private string NextName(string extension = ".md") => App.Time.GetLocalNow().ToString("yyyy-MM-dd_HH-mm") + extension;

    [Fact]
    public async Task Creating_a_file_and_a_directory()
    {
        HttpResponseMessage file = await Client.PostAsync(Users.Alice, "/docs?create=file");
        HttpResponseMessage again = await Client.PostAsync(Users.Alice, "/docs?create=file");
        HttpResponseMessage directory = await Client.PostAsync(Users.Alice, "/docs?create=directory");

        await AssertStatus(HttpStatusCode.Redirect, file);
        // A new file opens in the editor, a new directory is just opened
        Assert.Equal("/docs/" + NextName() + "?edit", file.Headers.Location?.ToString());
        Assert.Equal("/docs/" + NextName("_1.md") + "?edit", again.Headers.Location?.ToString());
        Assert.Equal("/docs/" + NextName(""), directory.Headers.Location?.ToString());
        Assert.True(Tree.IsFile("docs/" + NextName()));
        Assert.True(Tree.IsDirectory("docs/" + NextName("")));
    }

    [Fact]
    public async Task A_new_file_gets_an_entry_in_the_configuration_of_its_folder()
    {
        await Client.PostAsync(Users.Alice, "/mixed?create=file");

        Assert.Contains(NextName(), Tree.ReadConfig("mixed"));
    }

    [Fact]
    public async Task Creating_needs_create_on_the_new_path_and_read_on_the_directory()
    {
        await AssertStatus(HttpStatusCode.Redirect, await Client.PostAsync(Users.Alice, "/drop?create=file"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/shelf?create=file"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Bob, "/docs?create=file"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Anonymous, "/docs?create=directory"));
        // Known, but alice may not read it: creating would tell her which names are taken
        HttpResponseMessage vault = await Client.PostAsync(Users.Alice, "/vault?create=file");
        await AssertStatus(HttpStatusCode.Forbidden, vault);
        // Told why, without naming anything inside
        Assert.NotEmpty(await BodyOf(vault));

        Assert.Equal(new[] { "existing.md", NextName() }.Order(), Directory.GetFiles(Tree.Physical("drop")).Select(Path.GetFileName).Where(n => n != ".config.json").Order());
        Assert.Single(Directory.GetFileSystemEntries(Tree.Physical("shelf")).Where(p => !p.EndsWith(".config.json")));
        Assert.Equal(3, Directory.GetFileSystemEntries(Tree.Physical("vault")).Length);
    }

    [Fact]
    public async Task The_exact_new_path_is_checked_not_just_the_directory()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), (NextName(), Cfg.Perms(cdDeny: ["g:team"]))));

        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/docs?create=file"));
        Assert.False(Tree.Exists("docs/" + NextName()));
    }

    [Theory]
    [InlineData("/docs?create=link")]
    [InlineData("/docs?create=")]
    [InlineData("/docs?create=FILE")]
    public async Task Unknown_kinds_are_refused(string url)
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, url));

    [Fact]
    public async Task Creating_in_a_file_is_not_a_thing()
        => await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Alice, "/docs/a.md?create=file"));

    // ---- Save

    [Fact]
    public async Task Saving_needs_write_on_the_file()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md", "new ü"));
        Assert.Equal("new ü", Tree.Read("docs/a.md"));

        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/mixed/view.md", "x"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/mixed/locked.md", "x"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Bob, "/docs/a.md", "x"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Anonymous, "/readme.md", "x"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Alice, "/docs", "x"));

        Assert.Equal("view", Tree.Read("mixed/view.md"));
        Assert.Equal("locked contents", Tree.Read("mixed/locked.md"));
        Assert.Equal("# Readme", Tree.Read("readme.md"));
    }

    [Fact]
    public async Task Write_without_create_still_saves()
        => await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/shelf/item.md", "changed"));

    // ---- Upload

    private async Task<string> StartUploadAsync(TestUser user, string directory)
    {
        HttpResponseMessage response = await Client.PostAsync(user, "/" + directory + "?upload=start");
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    private Task<HttpResponseMessage> ChunkAsync(TestUser user, string directory, string id, string name, long offset, bool last, string contents)
        => Client.PostBytesAsync(user, $"/{directory}?upload={id}&name={Uri.EscapeDataString(name)}&offset={offset}" + (last ? "&last" : ""), System.Text.Encoding.UTF8.GetBytes(contents));

    [Fact]
    public async Task Uploading_in_chunks()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");

        await AssertStatus(HttpStatusCode.OK, await ChunkAsync(Users.Alice, "docs", id, "up.md", 0, false, "hello "));
        HttpResponseMessage last = await ChunkAsync(Users.Alice, "docs", id, "up.md", 6, true, "world");

        await AssertStatus(HttpStatusCode.OK, last);
        Assert.Equal("docs/up.md", await BodyOf(last));
        Assert.Equal("hello world", Tree.Read("docs/up.md"));
    }

    [Fact]
    public async Task An_upload_never_overwrites()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");

        HttpResponseMessage response = await ChunkAsync(Users.Alice, "docs", id, "a.md", 0, true, "new");

        Assert.Equal("docs/a (1).md", await BodyOf(response));
        Assert.Equal("a", Tree.Read("docs/a.md"));
    }

    [Fact]
    public async Task Uploading_needs_read_on_the_directory_and_create_on_the_file()
    {
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/vault?upload=start"));

        string shelf = await StartUploadAsync(Users.Alice, "shelf");
        await AssertStatus(HttpStatusCode.Forbidden, await ChunkAsync(Users.Alice, "shelf", shelf, "x.md", 0, true, "x"));

        string bob = await StartUploadAsync(Users.Bob, "docs");
        await AssertStatus(HttpStatusCode.Forbidden, await ChunkAsync(Users.Bob, "docs", bob, "x.md", 0, true, "x"));

        Assert.False(Tree.Exists("shelf/x.md"));
        Assert.False(Tree.Exists("docs/x.md"));
    }

    [Fact]
    public async Task Anonymous_users_can_not_upload()
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await Client.PostAsync(Users.Anonymous, "/docs?upload=start");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.NotEmpty(await BodyOf(response));
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task The_name_the_file_actually_gets_is_checked_again()
    {
        // open.md is taken, so the upload becomes "open (1).md", which team may not create
        Tree.Config("mixed", Cfg.Folder(Cfg.Perms(), ("open (1).md", Cfg.Perms(cdDeny: ["g:team"]))));
        string id = await StartUploadAsync(Users.Alice, "mixed");

        HttpResponseMessage response = await ChunkAsync(Users.Alice, "mixed", id, "open.md", 0, true, "x");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.False(Tree.Exists("mixed/open (1).md"));
        // ...and what was uploaded is gone
        Assert.Equal(HttpStatusCode.Conflict, (await ChunkAsync(Users.Alice, "mixed", id, "other.md", 0, true, "x")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" a.md")]
    [InlineData("a.md ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape.md")]
    [InlineData("..\\escape.md")]
    [InlineData("a/b.md")]
    [InlineData("_")]
    [InlineData(".config.json")]
    [InlineData(".CONFIG.JSON")]
    [InlineData("a.md.")]
    public async Task Invalid_names_are_refused(string name)
    {
        string before = Tree.Snapshot();
        string id = await StartUploadAsync(Users.Alice, "docs");

        await AssertStatus(HttpStatusCode.BadRequest, await ChunkAsync(Users.Alice, "docs", id, name, 0, true, """{ "perms": { "inherit": false, "rwAllow": ["u:alice"] }, "files": [] }"""));

        Assert.Equal(before, Tree.Snapshot());
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(App.NotesDir)!, "escape.md")));
    }

    /// <summary>On Windows, "x." and "x " name the same file as "x". No such spelling may lead to a configuration file.</summary>
    [Theory]
    [InlineData(".config.json.")]
    [InlineData(".config.json..")]
    [InlineData(".config.json. .")]
    public async Task No_other_spelling_of_the_configuration_file_can_be_uploaded(string name)
    {
        string id = await StartUploadAsync(Users.Alice, "docs");

        await ChunkAsync(Users.Alice, "docs", id, name, 0, true, """{ "perms": { "inherit": false, "rwAllow": ["u:alice"], "cdAllow": ["u:alice"] }, "files": [] }""");

        Assert.Null(Tree.ReadConfig("docs"));
        // ...and so nothing about who may do what in docs changed
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Carol, "/docs"));
    }

    [Fact]
    public async Task The_upload_of_another_user_can_not_be_continued_or_aborted()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");
        await ChunkAsync(Users.Alice, "docs", id, "mine.md", 0, false, "alice's ");

        await AssertStatus(HttpStatusCode.Conflict, await ChunkAsync(Users.Carol, "docs", id, "mine.md", 8, true, "evil"));
        await Client.PostAsync(Users.Carol, $"/docs?upload={id}&abort");

        HttpResponseMessage last = await ChunkAsync(Users.Alice, "docs", id, "mine.md", 8, true, "upload");
        await AssertStatus(HttpStatusCode.OK, last);
        Assert.Equal("alice's upload", Tree.Read("docs/mine.md"));
    }

    /// <summary>Aborting is best effort (the page sends it when an upload fails), so an upload that is gone already is no error of the server.</summary>
    [Fact]
    public async Task Aborting_an_upload_that_does_not_exist_is_not_a_server_error()
        => await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Carol, $"/docs?upload={Guid.NewGuid():N}&abort"));

    [Theory]
    [InlineData("?upload=not-a-guid&name=x.md&offset=0&last", HttpStatusCode.BadRequest)]
    [InlineData("?upload=../../x&name=x.md&offset=0&last", HttpStatusCode.BadRequest)]
    [InlineData("?upload=00000000000000000000000000000001&name=x.md&offset=-1&last", HttpStatusCode.BadRequest)]
    [InlineData("?upload=00000000000000000000000000000001&name=x.md&offset=x&last", HttpStatusCode.BadRequest)]
    [InlineData("?upload=00000000000000000000000000000001&name=x.md&offset=0&last", HttpStatusCode.Conflict)]
    public async Task Malformed_chunks_are_refused(string query, HttpStatusCode expected)
        => await AssertStatus(expected, await Client.PostBytesAsync(Users.Alice, "/docs" + query, [1]));

    [Fact]
    public async Task A_chunk_that_leaves_a_gap_is_refused()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");
        await ChunkAsync(Users.Alice, "docs", id, "x.md", 0, false, "abc");

        await AssertStatus(HttpStatusCode.Conflict, await ChunkAsync(Users.Alice, "docs", id, "x.md", 100, true, "x"));
    }

    [Fact]
    public async Task Uploading_into_a_file_is_not_a_thing()
        => await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Alice, "/docs/a.md?upload=start"));

    [Fact]
    public async Task An_uploaded_file_gets_an_entry_in_the_configuration_of_its_folder()
    {
        string id = await StartUploadAsync(Users.Alice, "mixed");
        await ChunkAsync(Users.Alice, "mixed", id, "new.md", 0, true, "x");

        Assert.Contains("new.md", Tree.ReadConfig("mixed"));
    }
}
