using System.Net;

namespace Notes.Tests.Integration;

/// <summary>Uploading into folders inside the directory, which are made if they aren't there (security specification, Operations: Upload).</summary>
public sealed class UploadFolderTests(NotesAppFactory app) : IntegrationTest(app)
{
    private async Task<HttpResponseMessage> UploadAsync(TestUser user, string directory, string folder, string name, string contents = "x")
    {
        HttpResponseMessage start = await Client.PostAsync(user, "/" + directory + "?upload=start");
        await AssertStatus(HttpStatusCode.OK, start);
        string id = await BodyOf(start);
        return await Client.PostBytesAsync(user, $"/{directory}?upload={id}&name={Uri.EscapeDataString(name)}&folder={Uri.EscapeDataString(folder)}"
            + "&offset=0&last", System.Text.Encoding.UTF8.GetBytes(contents));
    }

    [Fact]
    public async Task Folders_that_are_not_there_are_made()
    {
        HttpResponseMessage response = await UploadAsync(Users.Alice, "docs", "assets/clips", "a.png", "image");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("docs/assets/clips/a.png", await BodyOf(response));
        Assert.Equal("image", Tree.Read("docs/assets/clips/a.png"));
        // Like any new folder, without a configuration: what is in it has the permissions of the directory
        Assert.Null(Tree.ReadConfig("docs/assets/clips"));
    }

    [Fact]
    public async Task The_file_gets_an_entry_in_the_configuration_of_the_folder_it_goes_into()
    {
        _ = await UploadAsync(Users.Alice, "", "mixed", "new.md");

        Assert.Contains("new.md", Tree.ReadConfig("mixed"));
    }

    [Fact]
    public async Task A_folder_that_is_there_is_used_in_any_case()
    {
        HttpResponseMessage response = await UploadAsync(Users.Alice, "docs", "NESTED", "b.md");

        // Into the folder as it is spelled, next to the file that was there
        Assert.Equal("docs/nested/b (1).md", await BodyOf(response));
        Assert.True(Tree.IsFile("docs/nested/b (1).md"));
        Assert.Equal("b", Tree.Read("docs/nested/b.md"));
    }

    [Fact]
    public async Task Making_a_folder_needs_create_on_it()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("assets", Cfg.Perms(cdDeny: ["g:team"]))));
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.Forbidden, await UploadAsync(Users.Alice, "docs", "assets", "a.png"));
        await AssertStatus(HttpStatusCode.Forbidden, await UploadAsync(Users.Alice, "shelf", "new", "a.png"));

        Assert.Equal(before, Tree.Snapshot());
    }

    [Theory]
    [InlineData("", "vault")]
    [InlineData("mixed", "closed")]
    public async Task A_folder_that_may_not_be_read_is_not_gone_into(string directory, string folder)
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await UploadAsync(Users.Alice, directory, folder, "secret.md");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task A_file_is_no_folder()
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.Conflict, await UploadAsync(Users.Alice, "docs", "a.md", "x.png"));

        Assert.Equal(before, Tree.Snapshot());
    }

    [Theory]
    [InlineData("..")]
    [InlineData("assets/..")]
    [InlineData("../team")]
    [InlineData(".")]
    [InlineData("_")]
    [InlineData("a\\..\\..")]
    [InlineData(".config.json")]
    [InlineData(" assets")]
    [InlineData("assets.")]
    [InlineData("a:b")]
    public async Task Invalid_folder_names_are_refused(string folder)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await UploadAsync(Users.Alice, "docs", folder, "x.png"));

        Assert.Equal(before, Tree.Snapshot());
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(App.NotesDir)!, "x.png")));
    }
}
