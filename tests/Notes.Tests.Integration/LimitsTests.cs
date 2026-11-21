using System.Net;
using System.Text;

namespace Notes.Tests.Integration;

/// <summary>
/// The limits of what users send and make (<c>Config:Limits</c>, docs/development/user-input.md), with the small ones of
/// <see cref="LimitedNotesAppFactory"/>.
/// </summary>
public sealed class LimitsTests(LimitedNotesAppFactory app) : IntegrationTest(app), IClassFixture<LimitedNotesAppFactory>
{
    private async Task<string> StartUploadAsync(TestUser user, string directory)
    {
        HttpResponseMessage response = await Client.PostAsync(user, "/" + directory + "?upload=start");
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    private Task<HttpResponseMessage> ChunkAsync(TestUser user, string directory, string id, string name, long offset, bool last, string contents)
        => Client.PostBytesAsync(user, $"/{directory}?upload={id}&name={Uri.EscapeDataString(name)}&offset={offset}" + (last ? "&last" : ""), Encoding.UTF8.GetBytes(contents));

    // ---- Uploads

    [Fact]
    public async Task An_upload_up_to_the_largest_size_is_taken()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");

        await AssertStatus(HttpStatusCode.OK, await ChunkAsync(Users.Alice, "docs", id, "fits.md", 0, true, new string('x', LimitedNotesAppFactory.MaxUploadBytes)));
        Assert.True(Tree.IsFile("docs/fits.md"));
    }

    [Fact]
    public async Task An_upload_larger_than_that_is_refused_and_discarded()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");

        await AssertStatus(HttpStatusCode.OK, await ChunkAsync(Users.Alice, "docs", id, "big.md", 0, false, "123456"));
        await AssertStatus(HttpStatusCode.RequestEntityTooLarge, await ChunkAsync(Users.Alice, "docs", id, "big.md", 6, true, "789012"));

        Assert.False(Tree.Exists("docs/big.md"));
        // Discarded: the upload can't go on
        await AssertStatus(HttpStatusCode.Conflict, await ChunkAsync(Users.Alice, "docs", id, "big.md", 0, true, "1"));
    }

    [Fact]
    public async Task Only_so_many_uploads_can_be_unfinished_at_once()
    {
        string first = await StartUploadAsync(Users.Alice, "docs");
        for (int i = 1; i < LimitedNotesAppFactory.MaxUploadsInProgressPerUser; i++)
            _ = await StartUploadAsync(Users.Alice, "docs");

        await AssertStatus(HttpStatusCode.TooManyRequests, await Client.PostAsync(Users.Alice, "/docs?upload=start"));

        // Another user has their own, and finishing one makes room
        _ = await StartUploadAsync(Users.Bob, "docs");
        await AssertStatus(HttpStatusCode.OK, await ChunkAsync(Users.Alice, "docs", first, "done.md", 0, true, "x"));
        _ = await StartUploadAsync(Users.Alice, "docs");
    }

    // ---- Paths

    [Fact]
    public async Task A_new_entry_can_not_get_too_long_a_path()
    {
        // "docs/" and a name of 251 bytes are 256 bytes, one more than the longest path
        string name = new string('n', 248) + ".md";
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, "/docs/a.md?rename=" + name));
        string id = await StartUploadAsync(Users.Alice, "docs");
        await AssertStatus(HttpStatusCode.BadRequest, await ChunkAsync(Users.Alice, "docs", id, name, 0, true, "x"));
        Assert.Equal(before, Tree.Snapshot());

        // A little shorter, it fits
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md?rename=" + name[..^5] + ".md"));
    }

    [Fact]
    public async Task A_directory_can_not_be_renamed_so_that_something_in_it_gets_too_long_a_path()
    {
        // "docs/" and "/b.md" are 10 bytes: with the directory's new name, b.md's path is 256
        string name = new string('d', 246);
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, "/docs/nested?rename=" + name));
        Assert.Equal(before, Tree.Snapshot());
    }

    // ---- Text

    [Fact]
    public async Task Text_longer_than_the_limit_is_not_saved()
    {
        await AssertStatus(HttpStatusCode.RequestEntityTooLarge, await Client.PostAsync(Users.Alice, "/docs/a.md", new string('x', LimitedNotesAppFactory.MaxTextBytes + 1)));
        Assert.Equal("a", Tree.Read("docs/a.md"));

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md", new string('x', LimitedNotesAppFactory.MaxTextBytes)));
    }

    [Fact]
    public async Task Text_longer_than_the_limit_is_not_shown_but_can_be_downloaded()
    {
        string contents = "# " + new string('x', LimitedNotesAppFactory.MaxTextBytes);
        _ = Tree.File("docs/long.md", contents);

        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/long.md"));
        Assert.Contains("id=\"unviewable\"", page);
        Assert.DoesNotContain(contents, page);
        Assert.Equal(contents, await BodyOf(await Client.GetAsync(Users.Alice, "/docs/long.md?download")));
    }
}
