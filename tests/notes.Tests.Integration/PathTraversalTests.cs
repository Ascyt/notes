using System.Net;

namespace notes.Tests.Integration;

/// <summary>
/// Trying to get out of the notes directory, or into what is reserved in it. The paths are sent as they are (past the URL parsing of a client,
/// which would resolve them), so this is what the app itself does with them, whatever a proxy or the server lets through.
/// </summary>
public sealed class PathTraversalTests : IntegrationTest, IDisposable
{
    private readonly string _outside;

    public PathTraversalTests(NotesAppFactory app) : base(app)
    {
        // Next to the notes: what a way out would reach
        _outside = Path.Combine(Path.GetDirectoryName(App.NotesDir)!, "outside-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "OUTSIDE SECRET");
        _ = Tree.File("_/temp/user/alice-id/0123.part", "someone's upload");
    }

    public void Dispose() => Directory.Delete(_outside, recursive: true);

    private string Out => Path.GetFileName(_outside);

    public static TheoryData<string> Paths()
    {
        TheoryData<string> paths = [];
        foreach (string path in new[]
                 {
                     "/../{out}/secret.txt", "/..\\{out}\\secret.txt", "/docs/../../{out}/secret.txt", "/docs/..\\..\\{out}\\secret.txt",
                     "/%2e%2e/{out}/secret.txt", "/%2E%2E%2F{out}%2Fsecret.txt", "/..%2f{out}%2fsecret.txt", "/..%5c{out}%5csecret.txt",
                     "/%252e%252e/{out}/secret.txt", "/....//{out}/secret.txt", "/.../{out}/secret.txt",
                     "/{abs}/secret.txt", "/{abs}\\secret.txt", "//?/{abs}/secret.txt", "/\\\\?\\{abs}\\secret.txt", "/file:///{abs}/secret.txt",
                     "/C:/Windows/win.ini", "/C:\\Windows\\win.ini", "/c:/windows/system32/drivers/etc/hosts", "//localhost/c$/Windows/win.ini",
                     "/etc/passwd", "/../../../../../../etc/passwd", "/~/x",
                     "/_", "/_/temp", "/_/temp/user/alice-id/0123.part", "/_\\temp", "/%5f/temp", "/./_/temp", "/docs/../_/temp",
                     "/.config.json", "/docs/../.config.json", "/mixed/.config.json", "/mixed/.CONFIG.JSON", "/mixed/.config.json.",
                     "/mixed/.config.json ", "/mixed/.config.json::$DATA", "/mixed/.config.json:x", "/mixed/.config.json%00",
                     "/mixed/closed/../closed/hidden.md", "/mixed/./closed/hidden.md",
                     "/docs/a.md%00.png", "/docs/a.md\0.png", "/docs/a.md/..", "/docs/a.md/../../vault/secret.md",
                     "/docs/a.md::$DATA", "/docs/A.MD::$DATA", "/docs/a.md.", "/docs/a.md ", "/docs/ a.md", "/docs/a~1.md", "/DOCS~1/a.md",
                     "/docs/nested/..\\..\\vault\\secret.md"
                 })
            paths.Add(path);
        return paths;
    }

    private string Resolve(string path)
        => path.Replace("{out}", Out).Replace("{abs}", _outside.TrimStart('/'));

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task No_way_out_and_no_way_into_reserved_or_hidden_entries(string template)
    {
        string path = Resolve(template);
        string before = Tree.Snapshot();

        foreach (TestUser user in new[] { Users.Alice, Users.Anonymous, Users.Admin })
        {
            foreach ((string method, string query, byte[]? body) in new (string, string, byte[]?)[]
                     {
                         ("GET", "", null), ("GET", "?raw", null), ("GET", "?download", null), ("POST", "", "pwned"u8.ToArray()), ("DELETE", "", null),
                         ("POST", "?rename=renamed", null), ("POST", "?create=file", null), ("GET", "?config", null)
                     })
            {
                RawResponse response = await Client.RawAsync(user, method, path, query, body);

                string what = $"{user} {method} {path}{query}";
                Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.True((int)response.StatusCode is >= 400 and < 500, $"{what}: {(int)response.StatusCode} {response.Body}");
                Assert.DoesNotContain("OUTSIDE SECRET", response.Body);
                Assert.DoesNotContain("someone's upload", response.Body);
                Assert.DoesNotContain("hidden contents", response.Body);
                Assert.DoesNotContain("the secret", response.Body);
                Assert.DoesNotContain("\"perms\"", response.Body);
            }
        }

        Assert.Equal(before, Tree.Snapshot());
        Assert.Equal("OUTSIDE SECRET", File.ReadAllText(Path.Combine(_outside, "secret.txt")));
        Assert.Single(Directory.EnumerateFileSystemEntries(_outside));
    }

    [Theory]
    [InlineData("/vault//secret.md")]
    [InlineData("/VAULT/SECRET.MD")]
    [InlineData(@"/vault\secret.md")]
    [InlineData("/mixed/closed//hidden.md/")]
    public async Task Other_spellings_of_hidden_paths_are_hidden_as_well(string path)
    {
        foreach (TestUser user in new[] { Users.Alice, Users.Anonymous })
        {
            RawResponse response = await Client.RawAsync(user, "GET", path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await Client.RawAsync(Users.Admin, "GET", path)).StatusCode);
    }

    [Fact]
    public async Task The_same_through_a_real_http_client()
    {
        // What a browser or curl would send: the client resolves the dots, so these end up as ordinary (missing) paths
        foreach (string url in new[] { $"/../{Out}/secret.txt", $"/%2e%2e/{Out}/secret.txt", $"/..%2f{Out}%2fsecret.txt", "/_/temp/user", "/.config.json" })
        {
            HttpResponseMessage response = await Client.GetAsync(Users.Admin, url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("OUTSIDE SECRET", await BodyOf(response));
        }
    }

    [Fact]
    public async Task Links_inside_the_notes_are_not_followed()
    {
        try
        {
            _ = Directory.CreateSymbolicLink(Tree.Physical("docs/link"), _outside);
            _ = File.CreateSymbolicLink(Tree.Physical("docs/link.txt"), Path.Combine(_outside, "secret.txt"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Symbolic links can't be made here (on Windows they need developer mode or admin rights).");
        }

        foreach (string path in new[] { "/docs/link", "/docs/link/secret.txt", "/docs/link.txt", "/docs/link.txt?raw", "/docs/link.txt?download" })
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, path));

        Assert.DoesNotContain("OUTSIDE SECRET", string.Join("", await ZipNamesAsync(await Client.GetAsync(Users.Admin, "/docs?download"))));
        HttpResponseMessage copy = await TransferAsync(Users.Admin, "copy", "team", "docs");
        Assert.False(copy.IsSuccessStatusCode);
    }
}
