using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Notes.Features.Components.Archive;
using Notes.Features.Components.Download;

namespace Notes.Tests.Integration;

/// <summary>
/// Unzipping an uploaded archive (<c>&amp;unzip</c>): it is uploaded as a file, which is then unzipped in the background into a new folder named
/// after it (<see cref="Notes.Features.Components.Upload.UploadedArchiveUnzipper"/>), asked about with <c>?unzipping</c>. Each test has an
/// app of its own.
/// </summary>
public sealed class UploadUnzipTests() : IntegrationTest(new NotesAppFactory()), IAsyncDisposable
{
    private const string Secret = "zip secret";

    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    /// <summary>An archive of the files (path in it, contents), in the format, encrypted with the password if there is one.</summary>
    private static async Task<byte[]> ArchiveOf(ArchiveFormat format, string? password, params (string Path, string Contents)[] files)
    {
        // (Each path by its parts: one part is a file, more a folder with what is in it)
        static ZipFolderEntry FolderOf(string name, List<(string[] Parts, string Contents)> inside) => new(name, () =>
            [.. inside.GroupBy(file => file.Parts[0]).Select(group => group.First() is { Parts.Length: 1 } file
                ? (ZipEntry)new ZipFileEntry(group.Key, DateTimeOffset.UtcNow, () => new MemoryStream(Encoding.UTF8.GetBytes(file.Contents)))
                : FolderOf(group.Key, [.. group.Select(file => (file.Parts[1..], file.Contents))]))]);
        ZipFolderEntry root = FolderOf("", [.. files.Select(file => (file.Path.Split('/'), file.Contents))]);

        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + (format == ArchiveFormat.Zip ? ".zip" : ".7z"));
        try
        {
            await ArchiveWriter.WriteAsync(root, new ArchiveOptions(format,
                format == ArchiveFormat.Zip ? ArchiveCompression.Store : ArchiveCompression.Min, password), target, CancellationToken.None);
            return await File.ReadAllBytesAsync(target);
        }
        finally
        {
            File.Delete(target);
        }
    }

    private async Task<string> StartAsync()
        => await BodyOf(await Client.PostAsync(Users.Alice, "/docs?upload=start"));

    /// <summary>The whole archive as the one (last) chunk, to be unzipped.</summary>
    private Task<HttpResponseMessage> UploadToUnzipAsync(string id, string name, byte[] bytes)
        => Client.PostBytesAsync(Users.Alice, $"/docs?upload={id}&name={Uri.EscapeDataString(name)}&offset=0&last&unzip", bytes);

    /// <summary>Uploads it to be unzipped, and waits until that ended: how it ended (<c>?unzipping</c>).</summary>
    private async Task<JsonNode> UploadAndUnzipAsync(string name, byte[] bytes)
    {
        HttpResponseMessage uploaded = await UploadToUnzipAsync(await StartAsync(), name, bytes);
        await AssertStatus(HttpStatusCode.OK, uploaded);
        Assert.Equal("docs/" + name, await BodyOf(uploaded));
        return await UnzippingEndedAsync(name);
    }

    private async Task<JsonNode> UnzippingEndedAsync(string name)
    {
        for (DateTime giveUp = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < giveUp; await Task.Delay(50))
        {
            JsonNode state = await JsonOf(await Client.GetAsync(Users.Alice, "/docs?unzipping=" + Uri.EscapeDataString(name)));
            if ((string?)state["state"] != "unzipping")
                return state;
        }
        throw new TimeoutException(name + " was still being unzipped");
    }

    private static async Task<JsonNode> JsonOf(HttpResponseMessage response)
    {
        await AssertStatus(HttpStatusCode.OK, response);
        return JsonNode.Parse(await BodyOf(response))!;
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.SevenZip)]
    public async Task It_is_uploaded_as_a_file_then_unzipped_into_a_new_folder_named_after_it(ArchiveFormat format)
    {
        string name = format == ArchiveFormat.Zip ? "photos.zip" : "photos.7z";
        byte[] archive = await ArchiveOf(format, null, ("a.md", "A"), ("deeper/b.md", "B"));

        JsonNode ended = await UploadAndUnzipAsync(name, archive);

        Assert.Equal("unzipped", (string)ended["state"]!);
        Assert.Equal("docs/photos", (string)ended["folder"]!);
        Assert.Equal("A", Tree.Read("docs/photos/a.md"));
        Assert.Equal("B", Tree.Read("docs/photos/deeper/b.md"));
        Assert.False(Tree.Exists("docs/" + name));
        Assert.DoesNotContain(name, Tree.ReadConfig("docs") ?? "");
    }

    [Fact]
    public async Task One_folder_around_all_of_it_is_left_out()
    {
        byte[] archive = await ArchiveOf(ArchiveFormat.Zip, null, ("photos/a.md", "A"), ("photos/deeper/b.md", "B"));

        _ = await UploadAndUnzipAsync("photos.zip", archive);

        Assert.Equal("A", Tree.Read("docs/photos/a.md"));
        Assert.Equal("B", Tree.Read("docs/photos/deeper/b.md"));
        Assert.False(Tree.Exists("docs/photos/photos"));
    }

    [Fact]
    public async Task A_taken_name_gets_a_number()
    {
        Tree.File("docs/photos/old.md", "old");
        byte[] archive = await ArchiveOf(ArchiveFormat.Zip, null, ("a.md", "A"));

        JsonNode ended = await UploadAndUnzipAsync("photos.zip", archive);

        Assert.Equal("docs/photos (1)", (string)ended["folder"]!);
        Assert.Equal("old", Tree.Read("docs/photos/old.md"));
        Assert.Equal("A", Tree.Read("docs/photos (1)/a.md"));
    }

    [Fact]
    public async Task Configurations_and_archives_of_archived_folders_in_it_are_left_out()
    {
        byte[] archive = await ArchiveOf(ArchiveFormat.Zip, null, ("a.md", "A"), (".config.json", "{}"), ("vault/_.7z", "x"),
            ("vault/.config.json", "{}"));

        JsonNode ended = await UploadAndUnzipAsync("with-configs.zip", archive);

        Assert.Equal(3, (int)ended["leftOut"]!);
        Assert.Equal("A", Tree.Read("docs/with-configs/a.md"));
        Assert.False(Tree.Exists("docs/with-configs/.config.json"));
        Assert.False(Tree.Exists("docs/with-configs/vault/_.7z"));
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.SevenZip)]
    public async Task An_encrypted_one_stays_a_file_and_its_entry_says_why(ArchiveFormat format)
    {
        string name = format == ArchiveFormat.Zip ? "secret.zip" : "secret.7z";
        byte[] archive = await ArchiveOf(format, Secret, ("a.md", "A"));

        JsonNode ended = await UploadAndUnzipAsync(name, archive);

        Assert.Equal("failed", (string)ended["state"]!);
        Assert.Contains("encrypted", (string)ended["error"]!);
        Assert.Equal(archive, File.ReadAllBytes(Tree.Physical("docs/" + name)));
        Assert.False(Tree.Exists("docs/secret"));
        Assert.Contains("unzipError", Tree.ReadConfig("docs"));
        // The listing marks it
        Assert.Contains("data-unzip-failed", await BodyOf(await Client.GetAsync(Users.Alice, "/docs")));
    }

    [Fact]
    public async Task A_damaged_one_stays_a_file_and_its_entry_says_why()
    {
        JsonNode ended = await UploadAndUnzipAsync("broken.zip", "not a zip at all"u8.ToArray());

        Assert.Equal("failed", (string)ended["state"]!);
        Assert.Contains("damaged", (string)ended["error"]!);
        Assert.True(Tree.Exists("docs/broken.zip"));
        Assert.False(Tree.Exists("docs/broken"));
    }

    [Fact]
    public async Task One_that_breaks_the_limits_stays_a_file()
    {
        byte[] archive = await ArchiveOf(ArchiveFormat.Zip, null, ("../evil.md", "x"), ("a.md", "A"));

        JsonNode ended = await UploadAndUnzipAsync("evil.zip", archive);

        Assert.Equal("failed", (string)ended["state"]!);
        Assert.True(Tree.Exists("docs/evil.zip"));
        Assert.False(Tree.Exists("docs/evil"));
        Assert.False(Tree.Exists("evil.md"));
    }

    [Fact]
    public async Task Without_unzip_an_archive_is_a_file_like_any_other()
    {
        byte[] archive = await ArchiveOf(ArchiveFormat.Zip, null, ("a.md", "A"));

        HttpResponseMessage uploaded = await Client.PostBytesAsync(Users.Alice, $"/docs?upload={await StartAsync()}&name=plain.zip&offset=0&last",
            archive);

        await AssertStatus(HttpStatusCode.OK, uploaded);
        Assert.True(Tree.Exists("docs/plain.zip"));
        Assert.Null((string?)(await UnzippingEndedAsync("plain.zip"))["state"]);
        Assert.False(Tree.Exists("docs/plain"));
    }

    [Fact]
    public async Task Unzip_means_nothing_for_a_file_that_is_no_archive()
    {
        HttpResponseMessage uploaded = await UploadToUnzipAsync(await StartAsync(), "notes.md", "text"u8.ToArray());

        await AssertStatus(HttpStatusCode.OK, uploaded);
        Assert.Equal("text", Tree.Read("docs/notes.md"));
        Assert.DoesNotContain("unzipPending", Tree.ReadConfig("docs") ?? "");
    }

    [Fact]
    public async Task Who_may_not_create_here_gets_nothing_unzipped()
    {
        byte[] archive = await ArchiveOf(ArchiveFormat.Zip, null, ("a.md", "A"));
        string id = await BodyOf(await Client.PostAsync(Users.Bob, "/docs?upload=start"));

        HttpResponseMessage refused = await Client.PostBytesAsync(Users.Bob, $"/docs?upload={id}&name=x.zip&offset=0&last&unzip", archive);

        await AssertStatus(HttpStatusCode.Forbidden, refused);
        Assert.False(Tree.Exists("docs/x"));
    }

    [Fact]
    public async Task Where_the_folder_may_not_be_made_the_archive_is_refused_before_it_is_sent()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("photos", Cfg.Perms(cdDeny: ["g:team"]))));
        byte[] archive = await ArchiveOf(ArchiveFormat.Zip, null, ("a.md", "A"));

        HttpResponseMessage refused = await UploadToUnzipAsync(await StartAsync(), "photos.zip", archive);

        await AssertStatus(HttpStatusCode.Forbidden, refused);
        Assert.Contains("photos", await BodyOf(refused));
    }

    [Fact]
    public async Task One_that_was_to_be_unzipped_when_the_app_stopped_is_unzipped_once_its_folder_is_listed()
    {
        // As the app leaves it: in place, and its entry says it is to be unzipped (nothing runs for it after a restart)
        File.WriteAllBytes(Tree.Physical("docs/later.zip"), await ArchiveOf(ArchiveFormat.Zip, null, ("a.md", "A")));
        Tree.Config("docs", Cfg.Folder(Cfg.Perms()));
        string config = Tree.ReadConfig("docs")!;
        File.WriteAllText(Tree.Physical("docs/.config.json"), Notes.Features.Configuration.ConfigText.SetFileProperty(config, "later.zip",
            Notes.Features.Configuration.ConfigParser.UnzipPendingKey, "true")!);

        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Contains("data-unzipping-name=\"later.zip\"", listing);

        Assert.Equal("unzipped", (string)(await UnzippingEndedAsync("later.zip"))["state"]!);
        Assert.Equal("A", Tree.Read("docs/later/a.md"));
    }

    [Fact]
    public async Task The_hashes_of_a_file_are_those_of_its_pieces()
    {
        const int piece = 1024 * 1024;
        byte[] contents = new byte[piece * 2 + 5];
        Random.Shared.NextBytes(contents);
        File.WriteAllBytes(Tree.Physical("docs/big.bin"), contents);

        JsonNode answer = await JsonOf(await Client.GetAsync(Users.Alice, $"/docs?hashes=big.bin&size={contents.Length}&piece={piece}"));

        string[] expected = [.. contents.Chunk(piece).Select(chunk => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(chunk)))];
        Assert.Equal(expected, answer["hashes"]!.AsArray().Select(hash => (string)hash!));
        // Another size, or no such file: nothing to compare with
        Assert.Null((await JsonOf(await Client.GetAsync(Users.Alice, $"/docs?hashes=big.bin&size=3&piece={piece}")))["hashes"]);
        Assert.Null((await JsonOf(await Client.GetAsync(Users.Alice, $"/docs?hashes=nothing.bin&size=3&piece={piece}")))["hashes"]);
    }

    [Fact]
    public async Task The_hashes_of_a_file_are_not_told_to_whoever_may_not_read_it()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("a.md", Cfg.Perms(inherit: false))));
        long size = new FileInfo(Tree.Physical("docs/a.md")).Length;

        JsonNode answer = await JsonOf(await Client.GetAsync(Users.Alice, $"/docs?hashes=a.md&size={size}&piece={1024 * 1024}"));

        Assert.Null(answer["hashes"]);
    }
}
