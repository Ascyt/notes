using System.Net;
using System.Text.Json.Nodes;
using Notes.Features.Passwords;

namespace Notes.Tests.Integration;

/// <summary>
/// Setting and changing passwords in the configuration, copies and moves that take them along, and the wait after wrong ones (security
/// specification, "Passwords").
/// </summary>
public sealed class PasswordChangeTests(NotesAppFactory app) : PasswordTest(app)
{
    private static string FolderWith(string passwordProperty)
        => $$"""
             {
                 "version": "{{Cfg.Version}}",
                 // a comment that stays
                 "perms": { "inherit": true },
                 "gitignore": false,
                 {{passwordProperty}}
                 "files": []
             }
             """;

    [Fact]
    public async Task A_password_is_saved_as_its_hash_only()
    {
        Tree.File("docs/new/note.md", "note");

        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/docs/new?config", FolderWith("\"password\": \"swordfish\","));

        await AssertStatus(HttpStatusCode.OK, response);
        string saved = Tree.ReadConfig("docs/new")!;
        Assert.DoesNotContain("swordfish", saved);
        Assert.Contains("// a comment that stays", saved);
        string hash = (string)JsonNode.Parse(saved, documentOptions: new System.Text.Json.JsonDocumentOptions
            { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!["passwordHash"]!;
        Assert.True(PasswordHashing.Verify("swordfish", hash));
        // What the editor shows next is what was saved
        Assert.Equal(saved, await BodyOf(response));
    }

    [Fact]
    public async Task Whoever_set_the_password_has_it_unlocked_everyone_else_is_asked()
    {
        Tree.File("docs/new/note.md", "note");
        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/docs/new?config", FolderWith("\"password\": \"swordfish\","));
        string cookie = response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])
            .Single(value => value.StartsWith(PasswordUnlocks.CookieName + "=", StringComparison.Ordinal));

        await AssertStatus(HttpStatusCode.OK, await GetWithCookieAsync(Users.Admin, "/docs/new/note.md", cookie));
        await AssertPasswordRequired("/docs/new", await Client.GetAsync(Users.Admin, "/docs/new/note.md"));
        await AssertPasswordRequired("/docs/new", await Client.GetAsync(Users.Alice, "/docs/new/note.md"));
    }

    [Theory]
    [InlineData("\"password\": \"\",")]
    [InlineData("\"password\": \"tab\\there\",")]
    [InlineData("\"password\": 42,")]
    [InlineData("\"passwordHash\": \"not a hash\",")]
    [InlineData("\"password\": \"a\", \"passwordHash\": \"$2a$04$abcdefghijklmnopqrstuuJ3h2c7Bqvf0QvF3x9pFVQb1Ll7sW6Gu\",")]
    public async Task What_can_not_be_a_password_is_refused(string property)
    {
        Tree.Dir("docs/new");

        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Admin, "/docs/new?config", FolderWith(property)));
        Assert.Null(Tree.ReadConfig("docs/new"));
    }

    [Fact]
    public async Task A_password_longer_than_72_bytes_is_refused_also_in_fewer_characters()
    {
        Tree.Dir("docs/new");
        // 37 characters, but 74 bytes in UTF-8
        string umlauts = new('ä', 37);

        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/docs/new?config", FolderWith($"\"password\": \"{umlauts}\","));

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Null(Tree.ReadConfig("docs/new"));
    }

    [Fact]
    public async Task A_password_written_into_the_file_by_hand_makes_it_invalid()
    {
        Tree.File("docs/new/note.md", "note");
        Tree.Config("docs/new", FolderWith("\"password\": \"swordfish\","));

        // Only admins have access where a configuration is invalid
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Alice, "/docs/new"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Admin, "/docs/new/note.md"));
        Assert.Contains("swordfish", Tree.ReadConfig("docs/new")!);
    }

    [Fact]
    public async Task The_password_of_a_file_is_set_in_its_own_configuration()
    {
        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/papers/plain.md?config",
            "{ \"perms\": { \"inherit\": true }, \"password\": \"pencil\" }");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.DoesNotContain("pencil", await BodyOf(response));
        Assert.Contains("passwordHash", await BodyOf(response));
        await AssertPasswordRequired("/papers/plain.md", await Client.GetAsync(Users.Alice, "/papers/plain.md"));
    }

    [Fact]
    public async Task Removing_the_password_of_a_file_needs_it_unlocked_also_for_admins()
    {
        string withoutPassword = Cfg.Folder(Cfg.Perms(inherit: true), ("pw.md", Cfg.Perms()), ("plain.md", Cfg.Perms()));

        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Admin, "/papers?config", withoutPassword));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Admin, "/papers/pw.md?config",
            "{ \"perms\": { \"inherit\": true } }"));
        await AssertPasswordRequired("/papers/pw.md", await Client.GetAsync(Users.Admin, "/papers/pw.md"));

        HttpResponseMessage unlocked = await WithPasswordAsync(Users.Admin, HttpMethod.Post, "/papers?config", FilePassword,
            new StringContent(withoutPassword));
        await AssertStatus(HttpStatusCode.OK, unlocked);
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/papers/pw.md"));
    }

    [Fact]
    public async Task A_new_password_needs_the_old_one_unlocked()
    {
        string newPassword = WithFilePassword(Cfg.Folder(Cfg.Perms(inherit: true), ("pw.md", Cfg.Perms()), ("plain.md", Cfg.Perms())), "pw.md",
            "new");

        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Admin, "/papers?config", newPassword));
        await AssertStatus(HttpStatusCode.OK, await GetWithPasswordAsync(Users.Admin, "/papers/pw.md", FilePassword));
    }

    [Fact]
    public async Task Saving_the_folder_unchanged_keeps_the_password_of_a_file_without_unlocking_it()
    {
        string saved = Tree.ReadConfig("papers")!;

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/papers?config", saved));
        await AssertPasswordRequired("/papers/pw.md", await Client.GetAsync(Users.Alice, "/papers/pw.md"));
    }

    [Fact]
    public async Task A_copied_file_keeps_its_password()
    {
        // (Selecting it needs it unlocked, as does copying it)
        long version = await SelectWithPassword("papers/pw.md", FilePassword);
        HttpResponseMessage copied = await WithPasswordAsync(Users.Alice, HttpMethod.Post, "/docs?transfer=copy", FilePassword,
            new StringContent($"{{\"version\":{version}}}", System.Text.Encoding.UTF8, "application/json"));

        await AssertStatus(HttpStatusCode.OK, copied);
        await AssertPasswordRequired("/docs/pw.md", await Client.GetAsync(Users.Alice, "/docs/pw.md"));
        await AssertStatus(HttpStatusCode.OK, await GetWithPasswordAsync(Users.Alice, "/docs/pw.md", FilePassword));
        // docs had no configuration: the one it got for the password changes nothing else
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Bob, "/docs/a.md"));
    }

    private async Task<long> SelectWithPassword(string path, string password)
    {
        HttpResponseMessage response = await WithPasswordAsync(Users.Alice, HttpMethod.Post, "/?selection=add", password,
            new StringContent($"{{\"paths\":[\"{path}\"]}}", System.Text.Encoding.UTF8, "application/json"));
        await AssertStatus(HttpStatusCode.OK, response);
        return System.Text.Json.JsonDocument.Parse(await BodyOf(response)).RootElement.GetProperty("version").GetInt64();
    }

    [Fact]
    public async Task A_copied_directory_keeps_its_password_and_so_does_what_is_inside()
    {
        Tree.File("safe/deeper/sealed.md", "sealed");
        Tree.Config("safe/deeper", WithFilePassword(Cfg.Folder(Cfg.Perms(inherit: true), ("sealed.md", Cfg.Perms()), ("more.md", Cfg.Perms())),
            "sealed.md", "inner"));

        long version = await SelectWithPassword("safe", SafePassword);
        HttpResponseMessage copied = await WithPasswordAsync(Users.Alice, HttpMethod.Post, "/docs?transfer=copy", SafePassword,
            new StringContent($"{{\"version\":{version}}}", System.Text.Encoding.UTF8, "application/json"));
        await AssertStatus(HttpStatusCode.OK, copied);

        // The inner file was locked by a password of its own that the request didn't bring: it was left out, like anything unreadable
        Assert.False(Tree.Exists("docs/safe/deeper/sealed.md"));
        Assert.True(Tree.Exists("docs/safe/deeper/more.md"));
        await AssertPasswordRequired("/docs/safe", await Client.GetAsync(Users.Alice, "/docs/safe/inside.md"));
        await AssertStatus(HttpStatusCode.OK, await GetWithPasswordAsync(Users.Alice, "/docs/safe/inside.md", SafePassword));
    }

    [Fact]
    public async Task A_moved_file_keeps_its_password()
    {
        long version = await SelectWithPassword("papers/pw.md", FilePassword);
        HttpResponseMessage moved = await WithPasswordAsync(Users.Alice, HttpMethod.Post, "/docs?transfer=move", FilePassword,
            new StringContent($"{{\"version\":{version}}}", System.Text.Encoding.UTF8, "application/json"));

        await AssertStatus(HttpStatusCode.OK, moved);
        Assert.False(Tree.Exists("papers/pw.md"));
        await AssertPasswordRequired("/docs/pw.md", await Client.GetAsync(Users.Alice, "/docs/pw.md"));
        await AssertStatus(HttpStatusCode.OK, await GetWithPasswordAsync(Users.Alice, "/docs/pw.md", FilePassword));
    }

    [Fact]
    public async Task A_renamed_directory_keeps_its_password_and_its_unlock()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/safe", SafePassword);
        HttpResponseMessage renamed = await PostWithCookieAsync(Users.Alice, "/safe?rename=strongbox", cookie!);

        await AssertStatus(HttpStatusCode.OK, renamed);
        await AssertPasswordRequired("/strongbox", await Client.GetAsync(Users.Alice, "/strongbox/inside.md"));
        await AssertStatus(HttpStatusCode.OK, await GetWithCookieAsync(Users.Alice, "/strongbox/inside.md", cookie!));
    }

    [Fact]
    public async Task Moving_a_directory_with_something_locked_inside_fails()
    {
        Tree.File("docs/box/sealed.md", "sealed");
        Tree.Config("docs/box", WithFilePassword(Cfg.Folder(Cfg.Perms(inherit: true), ("sealed.md", Cfg.Perms())), "sealed.md", "inner"));

        HttpResponseMessage moved = await TransferAsync(Users.Alice, "move", "team", "docs/box");

        await AssertStatus(HttpStatusCode.Forbidden, moved);
        Assert.True(Tree.Exists("docs/box/sealed.md"));
    }

    [Fact]
    public async Task Wrong_passwords_make_the_next_ones_wait_and_say_how_long()
    {
        // bob only reads; his count is his own
        for (int i = 0; i < 5; i++)
            await AssertStatus(HttpStatusCode.Forbidden, (await UnlockAsync(Users.Bob, "/safe", "wrong " + i)).Response);

        (HttpResponseMessage waiting, _) = await UnlockAsync(Users.Bob, "/safe", SafePassword);
        await AssertStatus(HttpStatusCode.TooManyRequests, waiting);
        Assert.NotNull(waiting.Headers.RetryAfter);
        // The header is no way around it
        await AssertStatus(HttpStatusCode.TooManyRequests, await GetWithPasswordAsync(Users.Bob, "/safe/inside.md", SafePassword));
        // Someone else isn't kept waiting
        await AssertStatus(HttpStatusCode.NoContent, (await UnlockAsync(Users.Carol, "/safe", SafePassword)).Response);

        App.Time.Advance(TimeSpan.FromSeconds(2));
        await AssertStatus(HttpStatusCode.NoContent, (await UnlockAsync(Users.Bob, "/safe", SafePassword)).Response);
    }

    [Fact]
    public async Task Locking_ends_every_unlock_of_the_browser()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/safe", SafePassword);
        await AssertStatus(HttpStatusCode.OK, await GetWithCookieAsync(Users.Alice, "/safe/inside.md", cookie!));

        await AssertStatus(HttpStatusCode.NoContent, await PostWithCookieAsync(Users.Alice, "/docs?lock", cookie!));

        await AssertPasswordRequired("/safe", await GetWithCookieAsync(Users.Alice, "/safe/inside.md", cookie!));
    }
}
