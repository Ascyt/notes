using System.Net;
using System.Net.Http.Json;
using ICSharpCode.SharpZipLib.Zip;

namespace Notes.Tests.Integration;

/// <summary>
/// Downloading a directory as an archive with the options of the dialog (security specification, Operations: "Download a directory as an
/// archive").
/// </summary>
public sealed class ArchiveDirectoryTests(NotesAppFactory app) : ArchiveTest(app)
{
    [Fact]
    public async Task A_7z_of_the_root_leaves_out_what_alice_may_not_read_and_says_so()
    {
        _ = Tree.File("_/temp/x");

        (PreparedArchiveDto prepared, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/", "directory");
        Dictionary<string, string> contents = SevenZipContents(bytes);

        Assert.True(prepared.LeftOutUnreadable);
        Assert.Equal("b", contents["docs/nested/b.md"]);
        Assert.Contains("team/plan.md", contents.Keys);
        Assert.Contains("mixed/open.md", contents.Keys);
        Assert.DoesNotContain(contents.Keys, name => name.StartsWith("vault", StringComparison.Ordinal));
        Assert.DoesNotContain(contents.Keys, name => name.StartsWith("broken", StringComparison.Ordinal));
        Assert.DoesNotContain(contents.Keys, name => name.StartsWith("mixed/closed", StringComparison.Ordinal));
        Assert.DoesNotContain("mixed/locked.md", contents.Keys);
        Assert.DoesNotContain(contents.Keys, name => name.Contains(".config.json", StringComparison.Ordinal));
        Assert.DoesNotContain(contents.Keys, name => name.StartsWith("_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_directory_with_nothing_unreadable_says_nothing_was_left_out_and_keeps_empty_folders()
    {
        _ = Tree.Dir("docs/empty");

        (PreparedArchiveDto prepared, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/docs", "directory", "zip", "medium");
        Dictionary<string, string> contents = ZipContents(bytes);

        Assert.False(prepared.LeftOutUnreadable);
        Assert.Equal("a", contents["a.md"]);
        Assert.Equal("b", contents["nested/b.md"]);
        Assert.Contains("empty/", contents.Keys);
    }

    [Fact]
    public async Task An_encrypted_7z_opens_only_with_its_password()
    {
        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/docs", "directory", "7z", "max", "correct horse");

        Assert.Equal("a", SevenZipContents(bytes, "correct horse")["a.md"]);
        Assert.ThrowsAny<Exception>(() => SevenZipContents(bytes));
        Assert.ThrowsAny<Exception>(() => SevenZipContents(bytes, "wrong"));
    }

    [Fact]
    public async Task An_encrypted_zip_is_encrypted_with_aes_and_opens_only_with_its_password()
    {
        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/docs", "directory", "zip", "high", "correct horse");

        using (ZipFile zip = new(new MemoryStream(bytes)))
            Assert.All(zip.Cast<ZipEntry>().Where(entry => entry.IsFile), entry => Assert.Equal(256, entry.AESKeySize));
        Assert.Equal("a", ZipContents(bytes, "correct horse")["a.md"]);
        Assert.ThrowsAny<Exception>(() => ZipContents(bytes, "wrong"));
    }

    [Fact]
    public async Task A_stored_zip_is_not_compressed()
    {
        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/docs", "directory", "zip", "store");

        using ZipFile zip = new(new MemoryStream(bytes));
        Assert.All(zip.Cast<ZipEntry>().Where(entry => entry.IsFile), entry => Assert.Equal(CompressionMethod.Stored, entry.CompressionMethod));
    }

    [Theory]
    [InlineData("min")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("max")]
    public async Task Every_level_of_7z_makes_an_archive_that_opens(string compression)
    {
        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/docs", "directory", "7z", compression);

        Assert.Equal("b", SevenZipContents(bytes)["nested/b.md"]);
    }

    [Fact]
    public async Task An_admins_archive_has_the_configurations()
    {
        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Admin, "/mixed", "directory");

        Dictionary<string, string> contents = SevenZipContents(bytes);
        Assert.Contains(".config.json", contents.Keys);
        Assert.Contains("closed/hidden.md", contents.Keys);
    }

    [Fact]
    public async Task A_directory_that_is_known_but_unreadable_is_refused_and_a_hidden_one_answers_like_a_missing_one()
    {
        await AssertStatus(HttpStatusCode.Forbidden, await PrepareAsync(Users.Alice, "/vault", "directory"));

        HttpResponseMessage hidden = await PrepareAsync(Users.Alice, "/vault/inner", "directory");
        HttpResponseMessage missing = await PrepareAsync(Users.Alice, "/vault/nothing-here", "directory");
        await AssertStatus(HttpStatusCode.NotFound, hidden);
        Assert.Equal(await BodyOf(missing), await BodyOf(hidden));
    }

    [Fact]
    public async Task A_file_is_no_directory_to_archive()
        => await AssertStatus(HttpStatusCode.BadRequest, await PrepareAsync(Users.Alice, "/docs/a.md", "directory"));

    [Fact]
    public async Task Anonymous_users_get_no_archive_made_for_them()
    {
        HttpResponseMessage response = await PrepareAsync(Users.Anonymous, "/docs", "directory");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Empty(ArchiveFiles());
    }

    [Fact]
    public async Task An_archive_downloads_for_its_maker_only()
    {
        HttpResponseMessage prepared = await PrepareAsync(Users.Alice, "/docs", "directory");
        await AssertStatus(HttpStatusCode.OK, prepared);
        string id = (await prepared.Content.ReadFromJsonAsync<PreparedArchiveDto>(Cancellation))!.Id
            .ToString();

        HttpResponseMessage others = await Client.GetAsync(Users.Carol, "/docs?archive=" + id);
        HttpResponseMessage unknown = await Client.GetAsync(Users.Carol, "/docs?archive=" + Guid.NewGuid());
        await AssertStatus(HttpStatusCode.NotFound, others);
        Assert.Equal(await BodyOf(unknown), await BodyOf(others));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Anonymous, "/docs?archive=" + id));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs?archive=" + id));
    }

    [Fact]
    public async Task Only_the_newest_archives_of_a_user_are_kept()
    {
        List<string> ids = [];
        for (int i = 0; i < Notes.Features.Components.Archive.PreparedArchives.MaxKeptPerUser + 1; i++)
        {
            HttpResponseMessage prepared = await PrepareAsync(Users.Alice, "/docs", "directory", "zip", "store");
            await AssertStatus(HttpStatusCode.OK, prepared);
            ids.Add((await prepared.Content.ReadFromJsonAsync<PreparedArchiveDto>(Cancellation))!
                .Id.ToString());
        }

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs?archive=" + ids[0]));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs?archive=" + ids[^1]));
        Assert.Equal(Notes.Features.Components.Archive.PreparedArchives.MaxKeptPerUser, ArchiveFiles().Count);
    }

    [Fact]
    public async Task The_toolbar_of_a_logged_in_user_asks_how_to_make_the_archive_and_that_of_others_links_to_the_zip()
    {
        string alices = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        string anonymous = await BodyOf(await Client.GetAsync(Users.Anonymous, "/docs"));

        Assert.Contains("id=\"archive-modal\"", alices);
        Assert.DoesNotContain("href=\"?download\"", alices);
        Assert.DoesNotContain("id=\"archive-modal\"", anonymous);
        Assert.Contains("href=\"?download\"", anonymous);
    }
}
