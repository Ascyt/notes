using System.Net;

namespace notes.Tests.Integration;

/// <summary>
/// "Hidden entries answer like missing ones": a hidden path, given in the address of any request (every query, every method), gets the same
/// answer as a path that doesn't exist, and nothing is changed. (Security specification, "What a user may learn".)
/// </summary>
public sealed class HiddenEntriesTests(NotesAppFactory app) : IntegrationTest(app)
{
    public static TestUser UserNamed(string name) => name switch
    {
        "alice" => Users.Alice,
        "bob" => Users.Bob,
        "anonymous" => Users.Anonymous,
        "nobody" => Users.Nobody,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    /// <summary>
    /// For each user, entries hidden from them (inside directories they may not read; for anonymous users also those they may not read
    /// themselves).
    /// </summary>
    public static readonly (string User, string[] Paths)[] Hidden =
    [
        ("alice", BaseTree.HiddenFromAlice),
        ("bob", ["team/plan.md", "team/sub", "team/sub/c.md", "vault/secret.md", "mixed/closed/hidden.md", "broken/x.md"]),
        ("anonymous", ["team", "vault", "mixed/closed", "mixed/locked.md", "broken",
            "team/plan.md", "team/sub", "vault/inner/deep.md", "mixed/closed/hidden.md", "broken/x.md"]),
        ("nobody", ["readme.md", "docs", "docs/nested/b.md", "team", "vault"])
    ];

    /// <summary>Every kind of request there is: method and query.</summary>
    public static readonly (string Method, string Query, string? Body)[] Requests =
    [
        ("GET", "", null),
        ("GET", "?raw", null),
        ("GET", "?download", null),
        ("GET", "?config", null),
        ("GET", "?selection", null),
        ("HEAD", "", null),
        ("POST", "", "overwritten"),
        ("POST", "?config", """{ "perms": { "inherit": true }, "files": [] }"""),
        ("POST", "?rename=renamed.md", null),
        ("POST", "?create=file", null),
        ("POST", "?create=directory", null),
        ("POST", "?upload=start", null),
        ("POST", "?upload=00000000000000000000000000000001&name=x.md&offset=0&last", "x"),
        ("POST", "?transfer=copy", """{ "version": 0 }"""),
        ("POST", "?transfer=move", """{ "version": 0 }"""),
        ("POST", "?transfer=delete", """{ "version": 0 }"""),
        ("POST", "?selection=add", """{ "paths": ["docs/a.md"] }"""),
        ("POST", "?selection=clear", null),
        ("DELETE", "", null)
    ];

    public static TheoryData<string, string, string, string> Cases()
    {
        TheoryData<string, string, string, string> data = [];
        foreach ((string user, string[] paths) in Hidden)
            foreach (string path in paths)
                foreach ((string method, string query, _) in Requests)
                    data.Add(user, path, method, query);
        return data;
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(TestUser user, string method, string path, string query)
    {
        string? body = Requests.First(r => r.Method == method && r.Query == query).Body;
        HttpContent? content = body is null ? null : new StringContent(body, System.Text.Encoding.UTF8, body.StartsWith('{') ? "application/json" : "text/plain");
        HttpResponseMessage response = await Client.SendAsync(user, new HttpMethod(method), "/" + path + query, content);
        // The error page of a GET names the address and the time; what matters is everything else
        string text = method is "GET" or "HEAD" ? "" : await BodyOf(response);
        return (response.StatusCode, text);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_hidden_entry_answers_like_a_missing_one_and_nothing_changes(string userName, string hiddenPath, string method, string query)
    {
        TestUser user = UserNamed(userName);
        // Nobody is logged in, so the a of the base tree lets them read the root: take it away, so that the whole notes are hidden from them
        if (userName == "nobody")
            Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));
        string before = Tree.Snapshot();
        string missingSibling = (hiddenPath.Contains('/') ? hiddenPath[..hiddenPath.LastIndexOf('/')] + "/" : "") + "does-not-exist";

        (HttpStatusCode Status, string Body) hidden = await SendAsync(user, method, hiddenPath, query);
        (HttpStatusCode Status, string Body) missing = await SendAsync(user, method, missingSibling, query);
        (HttpStatusCode Status, string Body) missingElsewhere = await SendAsync(user, method, "does/not/exist", query);

        Assert.Equal(HttpStatusCode.NotFound, hidden.Status);
        Assert.Equal(missing, hidden);
        Assert.Equal(missingElsewhere, hidden);
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task Hidden_names_never_appear_in_what_alice_gets()
    {
        string[] pages = ["/", "/mixed", "/vault", "/broken", "/mixed/closed", "/?selection", "/mixed?download", "/?download"];
        foreach (string page in pages)
        {
            HttpResponseMessage response = await Client.GetAsync(Users.Alice, page);
            string body = page.Contains("download") ? string.Join("\n", await ZipNamesAsync(response)) : await BodyOf(response);
            foreach (string secret in BaseTree.SecretNamesForAlice)
                Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain("hidden contents", body);
            Assert.DoesNotContain("the secret", body);
        }
    }

}
