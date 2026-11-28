using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Notes.Features.Configuration;
using Notes.Features.Passwords;

namespace Notes.Tests.Unit;

/// <summary>Hashing passwords, reading them in the configuration, and how unlocks and wrong passwords are counted.</summary>
public sealed class PasswordTests
{
    // ---- Hashing

    [Fact]
    public void A_hash_checks_its_own_password_only()
    {
        string hash = PasswordHashing.Hash("swordfish", 4);

        Assert.True(PasswordHashing.IsHash(hash));
        Assert.True(PasswordHashing.Verify("swordfish", hash));
        Assert.False(PasswordHashing.Verify("Swordfish", hash));
        Assert.NotEqual(hash, PasswordHashing.Hash("swordfish", 4));
    }

    [Theory]
    [InlineData("", PasswordProblem.Empty)]
    [InlineData("tab\there", PasswordProblem.ControlCharacters)]
    [InlineData("ok", PasswordProblem.None)]
    public void What_can_be_a_password(string password, PasswordProblem expected)
        => Assert.Equal(expected, PasswordHashing.Check(password));

    [Fact]
    public void The_limit_is_72_bytes_not_characters()
    {
        Assert.Equal(PasswordProblem.None, PasswordHashing.Check(new string('a', 72)));
        Assert.Equal(PasswordProblem.TooLong, PasswordHashing.Check(new string('a', 73)));
        // 36 umlauts are 72 bytes, 37 are 74
        Assert.Equal(PasswordProblem.None, PasswordHashing.Check(new string('ä', 36)));
        Assert.Equal(PasswordProblem.TooLong, PasswordHashing.Check(new string('ä', 37)));
    }

    [Fact]
    public void A_password_longer_than_is_read_never_checks_out_also_if_what_would_be_read_is_right()
    {
        string hash = PasswordHashing.Hash(new string('a', 72), 4);

        Assert.False(PasswordHashing.Verify(new string('a', 73), hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("swordfish")]
    [InlineData("$2a$04$short")]
    [InlineData("$2a$99$abcdefghijklmnopqrstuuJ3h2c7Bqvf0QvF3x9pFVQb1Ll7sW6Gu")]
    public void What_is_no_hash(string text)
    {
        Assert.False(PasswordHashing.IsHash(text));
        Assert.False(PasswordHashing.Verify("swordfish", text));
    }

    // ---- In the configuration

    private const string WithPasswords = """
        {
            // the folder's
            "perms": { "inherit": true },
            "gitignore": false,
            "password": "folder one",
            "files": [
                { "name": "a.md", "perms": { "inherit": true }, "password": "file one" }, // a's
                { "name": "b.md", "perms": { "inherit": true } }
            ]
        }
        """;

    [Fact]
    public void Each_password_is_replaced_by_its_hash_where_it_was()
    {
        string? hashed = ConfigText.HashPasswords(WithPasswords, password => PasswordHashing.Hash(password, 4));

        Assert.NotNull(hashed);
        Assert.DoesNotContain("folder one", hashed);
        Assert.DoesNotContain("file one", hashed);
        Assert.Contains("// a's", hashed);
        FolderConfig config = ConfigParser.ParseFolder(hashed, existingFiles: null).Value!;
        Assert.True(PasswordHashing.Verify("folder one", config.PasswordHash!));
        Assert.True(PasswordHashing.Verify("file one", config.FindFile("a.md")!.PasswordHash!));
        Assert.Null(config.FindFile("b.md")!.PasswordHash);
    }

    [Fact]
    public void A_password_is_only_read_where_the_editor_hashes_it()
    {
        Assert.True(ConfigParser.ParseFolder(WithPasswords, existingFiles: null, allowsPlainPasswords: true).IsValid);
        Assert.False(ConfigParser.ParseFolder(WithPasswords, existingFiles: null).IsValid);
    }

    [Fact]
    public void Only_the_password_of_the_folder_and_of_the_entries_is_one()
    {
        // Anywhere else, a property of that name is unknown (the parser says so): it is left as it is
        const string text = "{ \"perms\": { \"inherit\": true, \"password\": \"x\" }, \"gitignore\": false, \"files\": [] }";

        Assert.Equal(text, ConfigText.HashPasswords(text, _ => "hash"));
        Assert.False(ConfigParser.ParseFolder(text, existingFiles: null, allowsPlainPasswords: true).IsValid);
    }

    [Fact]
    public void The_default_configurations_say_there_is_no_password_and_how_to_set_one()
    {
        string folder = ConfigText.Default(["a.md"], new Semver.SemVersion(1, 0, 0));
        string file = ConfigText.DefaultFileBody();

        Assert.Contains("\"password\": null", folder);
        Assert.Contains("\"password\": null", file);
        Assert.Null(ConfigParser.ParseFolder(folder, existingFiles: null).Value!.PasswordHash);
        Assert.True(ConfigParser.ParseFileBody(file).IsValid);
    }

    [Fact]
    public void A_password_of_null_is_none_and_stays_as_it_is()
    {
        const string text = """{ "perms": { "inherit": true }, "gitignore": false, "password": null, "files": [ { "name": "a.md", "perms": { "inherit": true }, "password": null } ] }""";

        Assert.Equal(text, ConfigText.HashPasswords(text, _ => "hash"));
        FolderConfig config = ConfigParser.ParseFolder(text, existingFiles: null).Value!;
        Assert.Null(config.PasswordHash);
        Assert.Null(config.FindFile("a.md")!.PasswordHash);
    }

    [Fact]
    public void A_password_carried_into_a_folder_takes_the_place_of_its_null()
    {
        string hash = PasswordHashing.Hash("x", 4);
        string? carried = ConfigText.SetPasswordHash(ConfigText.Default([], new Semver.SemVersion(1, 0, 0)), hash);

        Assert.NotNull(carried);
        Assert.DoesNotContain("\"password\": null", carried);
        Assert.Equal(hash, ConfigParser.ParseFolder(carried, existingFiles: null).Value!.PasswordHash);
    }

    // ---- Unlocks and wrong passwords

    private static HttpContext RequestWith(string? cookie, string? userId = null)
    {
        DefaultHttpContext context = new();
        if (cookie is not null)
            context.Request.Headers.Cookie = PasswordUnlocks.CookieName + "=" + cookie;
        if (userId is not null)
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("sub", userId)], "test"));
        return context;
    }

    private static string CookieOf(HttpContext context)
        => context.Response.Headers.SetCookie.ToString().Split(';')[0].Split('=')[1];

    [Fact]
    public void An_unlock_is_for_the_browser_and_user_that_made_it_and_runs_out_unless_used()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        PasswordUnlocks unlocks = new(new PasswordsConfig { UnlockMinutes = 10 }, time);
        Protection protection = new(@"C:\notes\safe", "hash");
        HttpContext first = RequestWith(null, "alice-id");
        unlocks.Unlock(first, protection);
        string cookie = CookieOf(first);

        Assert.True(unlocks.IsUnlocked(RequestWith(cookie, "alice-id"), protection));
        Assert.False(unlocks.IsUnlocked(RequestWith(cookie, "bob-id"), protection));
        Assert.False(unlocks.IsUnlocked(RequestWith(cookie), protection));
        Assert.False(unlocks.IsUnlocked(RequestWith("other", "alice-id"), protection));
        // Another password of the same entry is locked
        Assert.False(unlocks.IsUnlocked(RequestWith(cookie, "alice-id"), protection with { Hash = "new" }));

        time.Advance(TimeSpan.FromMinutes(9));
        Assert.True(unlocks.IsUnlocked(RequestWith(cookie, "alice-id"), protection));
        time.Advance(TimeSpan.FromMinutes(9));
        Assert.True(unlocks.IsUnlocked(RequestWith(cookie, "alice-id"), protection));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(unlocks.IsUnlocked(RequestWith(cookie, "alice-id"), protection));
    }

    [Fact]
    public void A_renamed_tree_keeps_what_was_unlocked_in_it()
    {
        PasswordUnlocks unlocks = new(new PasswordsConfig(), TimeProvider.System);
        HttpContext first = RequestWith(null, "alice-id");
        unlocks.Unlock(first, new Protection(@"C:\notes\safe", "outer"));
        string cookie = CookieOf(first);
        unlocks.Unlock(RequestWith(cookie, "alice-id"), new Protection(@"C:\notes\safe\inner.md", "inner"));
        unlocks.Unlock(RequestWith(cookie, "alice-id"), new Protection(@"C:\notes\safety", "other"));

        unlocks.TreeMoved(RequestWith(cookie, "alice-id"), @"C:\notes\safe", @"C:\notes\box");

        HttpContext request = RequestWith(cookie, "alice-id");
        Assert.True(unlocks.IsUnlocked(request, new Protection(@"C:\notes\box", "outer")));
        Assert.True(unlocks.IsUnlocked(request, new Protection(@"C:\notes\box\inner.md", "inner")));
        Assert.True(unlocks.IsUnlocked(request, new Protection(@"C:\notes\safety", "other")));
        Assert.False(unlocks.IsUnlocked(request, new Protection(@"C:\notes\safe", "outer")));
    }

    [Fact]
    public void Wrong_passwords_wait_longer_each_time_up_to_the_most()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        PasswordAttempts attempts = new(new PasswordsConfig { FreeAttempts = 2, MaxWaitSeconds = 4 }, time);
        Protection protection = new(@"C:\notes\safe", "hash");
        HttpContext alice = RequestWith(null, "alice-id");

        attempts.Wrong(alice, protection);
        attempts.Wrong(alice, protection);
        Assert.Equal(TimeSpan.FromSeconds(1), attempts.WaitBeforeNext(alice, protection));
        attempts.Wrong(alice, protection);
        Assert.Equal(TimeSpan.FromSeconds(2), attempts.WaitBeforeNext(alice, protection));
        attempts.Wrong(alice, protection);
        attempts.Wrong(alice, protection);
        Assert.Equal(TimeSpan.FromSeconds(4), attempts.WaitBeforeNext(alice, protection));

        // Each user, and each entry, counts for itself
        Assert.Equal(TimeSpan.Zero, attempts.WaitBeforeNext(RequestWith(null, "bob-id"), protection));
        Assert.Equal(TimeSpan.Zero, attempts.WaitBeforeNext(alice, protection with { PhysicalPath = @"C:\notes\other" }));

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.Zero, attempts.WaitBeforeNext(alice, protection));
        attempts.Right(alice, protection);
        attempts.Wrong(alice, protection);
        Assert.Equal(TimeSpan.Zero, attempts.WaitBeforeNext(alice, protection));
    }
}
