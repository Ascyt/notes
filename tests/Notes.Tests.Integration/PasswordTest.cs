using System.Net;
using System.Text.Json.Nodes;
using Notes.Features.Passwords;

namespace Notes.Tests.Integration;

/// <summary>
/// What the tests of passwords share: entries with a password on top of the <see cref="BaseTree"/>, and the ways a request brings the
/// password along (the cookie of an unlock, or the header).
/// <code>
/// /
///   safe/           password "open sesame" (no permissions of its own: as the root)
///     inside.md
///     deeper/more.md
///   papers/         as the root, but:
///     pw.md         password "letter" (in its entry)
///     plain.md
///   vault/pw/       password, inside what alice may not read: hidden from her, not asked for
/// </code>
/// </summary>
public abstract class PasswordTest : IntegrationTest
{
    protected const string SafePassword = "open sesame";
    protected const string FilePassword = "letter";

    protected PasswordTest(NotesAppFactory app) : base(app)
    {
        Tree.File("safe/inside.md", "# Inside\nsecret text");
        Tree.File("safe/deeper/more.md", "more");
        Tree.Config("safe", WithPassword(Cfg.Folder(Cfg.Perms(inherit: true), ("inside.md", Cfg.Perms())), SafePassword));
        Tree.File("papers/pw.md", "# Letter\nthe letter");
        Tree.File("papers/plain.md", "plain");
        Tree.Config("papers", WithFilePassword(Cfg.Folder(Cfg.Perms(inherit: true), ("pw.md", Cfg.Perms()), ("plain.md", Cfg.Perms())), "pw.md",
            FilePassword));
        Tree.File("vault/pw/x.md", "x");
        Tree.Config("vault/pw", WithPassword(Cfg.Folder(Cfg.Perms(inherit: true)), "vault"));
    }

    /// <summary>The configuration of a folder with the hash of a password of its own.</summary>
    protected static string WithPassword(string folderConfig, string password)
    {
        JsonObject root = JsonNode.Parse(folderConfig)!.AsObject();
        root["passwordHash"] = PasswordHashing.Hash(password, 4);
        return root.ToJsonString();
    }

    /// <summary>The configuration of a folder in which the entry of the file has the hash of a password.</summary>
    protected static string WithFilePassword(string folderConfig, string fileName, string password)
    {
        JsonObject root = JsonNode.Parse(folderConfig)!.AsObject();
        JsonObject entry = root["files"]!.AsArray().Select(file => file!.AsObject()).Single(file => (string?)file["name"] == fileName);
        entry["passwordHash"] = PasswordHashing.Hash(password, 4);
        return root.ToJsonString();
    }

    /// <summary>A request that brings the password along in the header (a POST with its body), encoded as the header wants it.</summary>
    protected Task<HttpResponseMessage> WithPasswordAsync(TestUser user, HttpMethod method, string url, string password,
        HttpContent? content = null)
        => WithHeaderAsync(user, method, url, Encoded(password), content);

    /// <summary>A request with the header as it is given (not encoded here).</summary>
    protected Task<HttpResponseMessage> WithHeaderAsync(TestUser user, HttpMethod method, string url, string headerValue,
        HttpContent? content = null)
        => Client.SendAsync(user, method, url, content, headers: new Dictionary<string, string> { [PasswordLocks.HeaderName] = headerValue });

    /// <summary>The password as the header carries it: base64 of its UTF-8.</summary>
    protected static string Encoded(string password) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));

    protected Task<HttpResponseMessage> GetWithPasswordAsync(TestUser user, string url, string password)
        => WithPasswordAsync(user, HttpMethod.Get, url, password);

    /// <summary>A GET with the cookie an unlock gave.</summary>
    protected Task<HttpResponseMessage> GetWithCookieAsync(TestUser user, string url, string cookie)
        => Client.SendAsync(user, HttpMethod.Get, url, headers: new Dictionary<string, string> { ["Cookie"] = cookie });

    /// <summary>A POST without a body, with the cookie an unlock gave (next to the antiforgery cookie).</summary>
    protected Task<HttpResponseMessage> PostWithCookieAsync(TestUser user, string url, string cookie)
    {
        AntiforgeryTokens tokens = App.TokensFor(user);
        return Client.SendAsync(user, HttpMethod.Post, url, antiforgery: false,
            headers: new Dictionary<string, string> { ["Cookie"] = cookie + "; " + tokens.Cookie, [tokens.HeaderName] = tokens.RequestToken });
    }

    /// <summary>Unlocks the entry with the password; the cookie it gave ("name=value"), or null if it wasn't unlocked.</summary>
    protected async Task<(HttpResponseMessage Response, string? Cookie)> UnlockAsync(TestUser user, string address, string password)
    {
        HttpResponseMessage response = await Client.PostJsonAsync(user, address + "?unlock", new { password });
        string? cookie = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
            ? cookies.Select(value => value.Split(';')[0]).FirstOrDefault(value => value.StartsWith(PasswordUnlocks.CookieName + "=", StringComparison.Ordinal))
            : null;
        return (response, cookie);
    }

    protected static string? RequiredPasswordOf(HttpResponseMessage response)
        => response.Headers.TryGetValues(PasswordGate.RequiredHeader, out IEnumerable<string>? values) ? values.Single() : null;

    protected static async Task AssertPasswordRequired(string address, HttpResponseMessage response)
    {
        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal(address, RequiredPasswordOf(response));
    }
}
