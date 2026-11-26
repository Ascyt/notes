using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ICSharpCode.SharpZipLib.Zip;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using Microsoft.Extensions.DependencyInjection;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Integration;

/// <summary>
/// Archives (<c>?archive=directory|selection</c>, made after the request that asked for them) of what holds an entry with a password: locked,
/// it is left out like what the user may not read; unlocked (in this browser, or by the header), it goes in. Each test has an app of its own.
/// <code>
/// outer/
///   open.md
///   locked/   password "inner"
///     x.md
/// </code>
/// </summary>
public sealed class PasswordArchiveTests : PasswordTest, IAsyncDisposable
{
    private const string InnerPassword = "inner";

    // (An app for each test: the archived folders are kept across the tests of one)
    public PasswordArchiveTests() : base(new NotesAppFactory())
    {
        Tree.File("outer/open.md", "open");
        Tree.File("outer/locked/x.md", "x");
        Tree.Config("outer/locked", WithPassword(Cfg.Folder(Cfg.Perms(inherit: true)), InnerPassword));
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    private sealed record Prepared(Guid Id, bool LeftOutUnreadable);

    /// <summary>Makes the archive (which must work) with the cookie if there is one, and the names of what is in it.</summary>
    private async Task<(Prepared Prepared, List<string> Names)> ArchiveAsync(string address, string kind, string format, string? cookie = null,
        string? headerPassword = null, long? version = null)
    {
        Dictionary<string, string> headers = [];
        AntiforgeryTokens tokens = App.TokensFor(Users.Alice);
        headers["Cookie"] = cookie is null ? tokens.Cookie : cookie + "; " + tokens.Cookie;
        headers[tokens.HeaderName] = tokens.RequestToken;
        if (headerPassword is not null)
            headers[Notes.Features.Passwords.PasswordLocks.HeaderName] = Encoded(headerPassword);
        // As the dialog asks: answered at once, so that the archive is made after the request ended, and asked how far it got until done
        headers["Prefer"] = "respond-async";
        HttpResponseMessage prepared = await Client.SendAsync(Users.Alice, HttpMethod.Post, address + "?archive=" + kind,
            JsonContent.Create(new { format, compression = format == "zip" ? "store" : "high", version }), antiforgery: false, headers: headers);
        await AssertStatus(HttpStatusCode.Accepted, prepared);
        Guid id = (await prepared.Content.ReadFromJsonAsync<Prepared>(Cancellation))!.Id;
        Prepared dto;
        for (DateTime giveUp = DateTime.UtcNow.AddSeconds(30); ; await Task.Delay(50))
        {
            Assert.True(DateTime.UtcNow < giveUp, "the archive was still being made");
            HttpResponseMessage progress = cookie is null
                ? await Client.GetAsync(Users.Alice, address + "?archive=" + id + "&progress")
                : await GetWithCookieAsync(Users.Alice, address + "?archive=" + id + "&progress", cookie);
            await AssertStatus(HttpStatusCode.OK, progress);
            JsonNode state = JsonNode.Parse(await BodyOf(progress))!;
            if ((bool)state["done"]!)
            {
                dto = new Prepared(id, (bool)state["leftOutUnreadable"]!);
                break;
            }
        }

        HttpResponseMessage download = cookie is null
            ? await Client.GetAsync(Users.Alice, address + "?archive=" + dto.Id)
            : await GetWithCookieAsync(Users.Alice, address + "?archive=" + dto.Id, cookie);
        await AssertStatus(HttpStatusCode.OK, download);
        byte[] bytes = await download.Content.ReadAsByteArrayAsync(Cancellation);
        return (dto, format == "zip" ? ZipNames(bytes) : SevenZipNames(bytes));
    }

    private static List<string> ZipNames(byte[] bytes)
    {
        using ZipFile zip = new(new MemoryStream(bytes));
        return [.. zip.Cast<ZipEntry>().Where(entry => !entry.IsDirectory).Select(entry => entry.Name)];
    }

    private static List<string> SevenZipNames(byte[] bytes)
    {
        using IArchive archive = SevenZipArchive.OpenArchive(new MemoryStream(bytes));
        return [.. archive.Entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Key!.Replace('\\', '/'))];
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task A_locked_folder_inside_is_left_out_and_says_so(string format)
    {
        (Prepared prepared, List<string> names) = await ArchiveAsync("/outer", "directory", format);

        Assert.Equal(["open.md"], names);
        Assert.True(prepared.LeftOutUnreadable);
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task Unlocked_in_this_browser_it_goes_in(string format)
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/outer/locked", InnerPassword);

        (Prepared prepared, List<string> names) = await ArchiveAsync("/outer", "directory", format, cookie: cookie);

        Assert.Equal(["locked/x.md", "open.md"], names.Order());
        Assert.False(prepared.LeftOutUnreadable);
    }

    [Fact]
    public async Task With_its_password_in_the_header_it_goes_in()
    {
        (_, List<string> names) = await ArchiveAsync("/outer", "directory", "7z", headerPassword: InnerPassword);

        Assert.Equal(["locked/x.md", "open.md"], names.Order());
    }

    [Fact]
    public async Task A_selected_folder_whose_password_is_unlocked_goes_in_with_what_is_in_it()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/outer/locked", InnerPassword);
        await AssertStatus(HttpStatusCode.OK, await Client.PostJsonAsync(Users.Alice, "/?selection=clear", new { paths = Array.Empty<string>() }));
        HttpResponseMessage added = await Client.PostJsonAsync(Users.Alice, "/?selection=add", new { paths = new[] { "outer/locked", "outer/open.md" } });
        long version = (long)JsonNode.Parse(await BodyOf(added))!["version"]!;

        (Prepared prepared, List<string> names) = await ArchiveAsync("/outer", "selection", "zip", cookie: cookie, version: version);

        Assert.Equal(["locked/x.md", "open.md"], names.Order());
        Assert.False(prepared.LeftOutUnreadable);
    }

    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();

    /// <summary>The configuration of an archived folder, with the password typed (as an admin saves it) if there is one.</summary>
    private static string ArchivedConfig(bool encrypted = false, string? typedPassword = null)
    {
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        if (typedPassword is not null)
            config["password"] = typedPassword;
        config["archive"] = new JsonObject { ["enabled"] = true, ["encrypted"] = encrypted };
        return config.ToJsonString();
    }

    /// <summary>name/ (archived, by an admin) with open.md, and locked/ inside with its password.</summary>
    private async Task ArchivedOuterAsync(string name)
    {
        Tree.File($"{name}/open.md", "open");
        Tree.File($"{name}/locked/x.md", "x");
        Tree.Config($"{name}/locked", WithPassword(Cfg.Folder(Cfg.Perms(inherit: true)), InnerPassword));
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, $"/{name}?config", ArchivedConfig()));
    }

    [Theory]
    [InlineData("7z", false)]
    [InlineData("zip", false)]
    [InlineData("7z", true)]
    [InlineData("zip", true)]
    public async Task In_an_archived_folder_a_locked_folder_inside_is_left_out_too(string format, bool zipped)
    {
        string name = $"archived-{format}-{zipped}";
        await ArchivedOuterAsync(name);
        if (zipped)
            Assert.True(await Archives.ZipAsync(Tree.Physical(name)));

        (Prepared prepared, List<string> names) = await ArchiveAsync("/" + name, "directory", format);

        Assert.Equal(["open.md"], names);
        Assert.True(prepared.LeftOutUnreadable);
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task In_an_archived_folder_an_unlocked_folder_inside_goes_in(string format)
    {
        string name = $"archived-unlocked-{format}";
        await ArchivedOuterAsync(name);
        Assert.True(await Archives.ZipAsync(Tree.Physical(name)));
        (_, string? cookie) = await UnlockAsync(Users.Alice, $"/{name}/locked", InnerPassword);

        (Prepared prepared, List<string> names) = await ArchiveAsync("/" + name, "directory", format, cookie: cookie);

        Assert.Equal(["locked/x.md", "open.md"], names.Order());
        Assert.False(prepared.LeftOutUnreadable);
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task An_encrypted_zipped_folder_inside_is_left_out_while_locked_and_goes_in_unlocked(string format)
    {
        string name = $"enc-outer-{format}";
        Tree.File($"{name}/open.md", "open");
        Tree.File($"{name}/vault/x.md", "x");
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, $"/{name}/vault?config", ArchivedConfig(true, InnerPassword)));
        Assert.True(await Archives.ZipAsync(Tree.Physical($"{name}/vault")));

        (Prepared locked, List<string> lockedNames) = await ArchiveAsync("/" + name, "directory", format);
        Assert.Equal(["open.md"], lockedNames);
        Assert.True(locked.LeftOutUnreadable);

        (_, string? cookie) = await UnlockAsync(Users.Alice, $"/{name}/vault", InnerPassword);
        (Prepared unlocked, List<string> unlockedNames) = await ArchiveAsync("/" + name, "directory", format, cookie: cookie);
        Assert.Equal(["open.md", "vault/x.md"], unlockedNames.Order());
        Assert.False(unlocked.LeftOutUnreadable);
    }

    [Fact]
    public async Task A_failure_is_said_in_plain_text_also_to_a_request_that_asks_for_json()
    {
        HttpResponseMessage gone = await Client.SendAsync(Users.Alice, HttpMethod.Get, "/outer?archive=" + Guid.NewGuid() + "&progress",
            headers: new Dictionary<string, string> { ["Accept"] = "application/json" });

        await AssertStatus(HttpStatusCode.NotFound, gone);
        Assert.Equal("text/plain", gone.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("\"", await BodyOf(gone));
    }
}
