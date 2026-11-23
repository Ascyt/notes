using System.Net;

namespace Notes.Tests.Integration;

/// <summary>What a password locks, for whom, and how a request gets past it (security specification, "Passwords").</summary>
public sealed class PasswordAccessTests(NotesAppFactory app) : PasswordTest(app)
{
    public static TheoryData<string> UsersWhoMayRead => new() { "admin", "alice", "anonymous" };

    private static TestUser UserNamed(string name) => name switch
    {
        "admin" => Users.Admin,
        "alice" => Users.Alice,
        _ => Users.Anonymous
    };

    [Theory]
    [MemberData(nameof(UsersWhoMayRead))]
    public async Task The_page_of_a_locked_directory_asks_for_its_password(string userName)
    {
        HttpResponseMessage response = await Client.GetAsync(UserNamed(userName), "/safe");

        await AssertPasswordRequired("/safe", response);
        string body = await BodyOf(response);
        Assert.Contains("id=\"password-prompt\"", body);
        Assert.DoesNotContain("inside.md", body);
    }

    [Theory]
    [MemberData(nameof(UsersWhoMayRead))]
    public async Task What_is_inside_a_locked_directory_asks_for_the_password_of_the_directory(string userName)
    {
        TestUser user = UserNamed(userName);
        await AssertPasswordRequired("/safe", await Client.GetAsync(user, "/safe/inside.md"));
        await AssertPasswordRequired("/safe", await Client.GetAsync(user, "/safe/deeper/more.md?raw"));
        // Whether something is there is part of what the password protects
        await AssertPasswordRequired("/safe", await Client.GetAsync(user, "/safe/missing.md"));
    }

    [Theory]
    [InlineData("?download")]
    [InlineData("?raw")]
    [InlineData("?thumbnail")]
    [InlineData("?history")]
    [InlineData("?before=2020-01-01T00:00:00Z")]
    public async Task Every_way_to_read_a_locked_file_asks_for_its_password(string query)
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/papers/pw.md" + query);

        await AssertPasswordRequired("/papers/pw.md", response);
        Assert.DoesNotContain("the letter", await BodyOf(response));
    }

    [Fact]
    public async Task Admins_are_asked_for_the_password_too_also_for_the_configuration()
    {
        await AssertPasswordRequired("/safe", await Client.GetAsync(Users.Admin, "/safe/inside.md"));
        await AssertPasswordRequired("/safe", await Client.GetAsync(Users.Admin, "/safe?config"));
        await AssertPasswordRequired("/safe", await Client.PostAsync(Users.Admin, "/safe?config", "{}"));
    }

    [Fact]
    public async Task Changes_inside_a_locked_directory_ask_for_its_password()
    {
        await AssertPasswordRequired("/safe", await Client.PostAsync(Users.Alice, "/safe/inside.md", "changed"));
        await AssertPasswordRequired("/safe", await Client.PostAsync(Users.Alice, "/safe?create=file&name=new.md"));
        await AssertPasswordRequired("/safe", await Client.DeleteAsync(Users.Alice, "/safe/inside.md"));
        await AssertPasswordRequired("/safe", await Client.PostAsync(Users.Alice, "/safe?rename=other"));

        Assert.Equal("# Inside\nsecret text", Tree.Read("safe/inside.md"));
        Assert.True(Tree.Exists("safe"));
    }

    [Fact]
    public async Task A_password_inside_what_the_user_may_not_read_is_hidden_not_asked_for()
    {
        // alice may not read vault: what is in it doesn't exist for her, password or not
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/pw"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/pw/x.md"));
        Assert.Null(RequiredPasswordOf(await Client.PostAsync(Users.Alice, "/vault/pw?unlock")));
    }

    [Fact]
    public async Task The_right_password_in_the_header_opens_it_for_that_request_only()
    {
        HttpResponseMessage response = await GetWithPasswordAsync(Users.Alice, "/safe/inside.md", SafePassword);
        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains("secret text", await BodyOf(response));

        await AssertPasswordRequired("/safe", await Client.GetAsync(Users.Alice, "/safe/inside.md"));
    }

    [Fact]
    public async Task A_wrong_password_in_the_header_opens_nothing()
    {
        await AssertPasswordRequired("/safe", await GetWithPasswordAsync(Users.Alice, "/safe/inside.md", "wrong"));
        // The password of another entry is no password of this one
        await AssertPasswordRequired("/safe", await GetWithPasswordAsync(Users.Alice, "/safe/inside.md", FilePassword));
    }

    [Fact]
    public async Task The_header_carries_any_password_in_base64()
    {
        const string password = "Schlüssel 🔑";
        Tree.Config("safe", WithPassword(Cfg.Folder(Cfg.Perms(inherit: true), ("inside.md", Cfg.Perms())), password));

        await AssertStatus(HttpStatusCode.OK, await GetWithPasswordAsync(Users.Alice, "/safe/inside.md", password));
    }

    [Fact]
    public async Task Passwords_joined_into_one_header_are_each_checked()
    {
        // A file with a password in a folder with one: both are needed
        Tree.File("safe/letter.md", "x");
        Tree.Config("safe", WithFilePassword(WithPassword(Cfg.Folder(Cfg.Perms(inherit: true), ("inside.md", Cfg.Perms()), ("letter.md", Cfg.Perms())),
            SafePassword), "letter.md", FilePassword));

        HttpResponseMessage response = await WithHeaderAsync(Users.Alice, HttpMethod.Get, "/safe/letter.md",
            Encoded(SafePassword) + ", " + Encoded(FilePassword));

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task A_password_in_the_header_that_isnt_encoded_opens_nothing()
    {
        await AssertPasswordRequired("/safe", await WithHeaderAsync(Users.Alice, HttpMethod.Get, "/safe/inside.md", SafePassword));
        // Base64, but not of UTF-8
        await AssertPasswordRequired("/safe", await WithHeaderAsync(Users.Alice, HttpMethod.Get, "/safe/inside.md", "/w=="));
    }

    [Fact]
    public async Task The_header_brings_a_change_past_the_password()
    {
        HttpResponseMessage response = await WithPasswordAsync(Users.Alice, HttpMethod.Post, "/safe/inside.md", SafePassword,
            new StringContent("changed", System.Text.Encoding.UTF8, "text/plain"));

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("changed", Tree.Read("safe/inside.md"));
    }

    [Theory]
    [MemberData(nameof(UsersWhoMayRead))]
    public async Task Unlocking_gives_a_cookie_that_opens_it(string userName)
    {
        TestUser user = UserNamed(userName);
        (HttpResponseMessage response, string? cookie) = await UnlockAsync(user, "/safe", SafePassword);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.NotNull(cookie);
        HttpResponseMessage opened = await GetWithCookieAsync(user, "/safe/inside.md", cookie);
        await AssertStatus(HttpStatusCode.OK, opened);
        Assert.Contains("secret text", await BodyOf(opened));
    }

    [Fact]
    public async Task The_cookie_of_one_user_unlocks_nothing_for_another()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/safe", SafePassword);

        await AssertPasswordRequired("/safe", await GetWithCookieAsync(Users.Carol, "/safe/inside.md", cookie!));
        await AssertPasswordRequired("/safe", await GetWithCookieAsync(Users.Anonymous, "/safe/inside.md", cookie!));
    }

    [Fact]
    public async Task A_wrong_password_unlocks_nothing()
    {
        (HttpResponseMessage response, string? cookie) = await UnlockAsync(Users.Alice, "/papers/pw.md", "wrong");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Null(cookie);
    }

    [Fact]
    public async Task A_password_longer_than_is_read_is_refused_not_cut_off()
    {
        // 73 bytes, of which the first 72 would be read by bcrypt
        string tooLong = new('a', 73);
        Tree.Config("safe", WithPassword(Cfg.Folder(Cfg.Perms(inherit: true), ("inside.md", Cfg.Perms())), new string('a', 72)));

        (HttpResponseMessage response, _) = await UnlockAsync(Users.Alice, "/safe", tooLong);
        await AssertStatus(HttpStatusCode.BadRequest, response);
        await AssertPasswordRequired("/safe", await GetWithPasswordAsync(Users.Alice, "/safe/inside.md", tooLong));
    }

    [Fact]
    public async Task Listings_show_what_a_password_locks_by_its_name_with_a_lock_of_its_own()
    {
        foreach (TestUser user in new[] { Users.Alice, Users.Anonymous, Users.Admin })
        {
            string root = await BodyOf(await Client.GetAsync(user, "/"));
            Assert.Contains("data-password-locked-entry=\"safe\"", root);

            string papers = await BodyOf(await Client.GetAsync(user, "/papers"));
            Assert.Contains("data-password-locked-entry=\"pw.md\"", papers);
            // Its title is part of what it holds: it is listed by its file name
            Assert.DoesNotContain(">Letter<", papers);
        }
    }

    [Fact]
    public async Task What_alice_may_not_read_anyway_stays_locked_not_password_locked()
    {
        string root = await BodyOf(await Client.GetAsync(Users.Alice, "/"));

        Assert.Contains("data-locked-entry=\"vault\"", root);
    }

    [Fact]
    public async Task A_download_of_the_directory_above_leaves_out_what_a_password_locks()
    {
        List<string> names = await ZipNamesAsync(await Client.GetAsync(Users.Alice, "/papers?download"));

        Assert.Contains("plain.md", names);
        Assert.DoesNotContain("pw.md", names);
    }

    [Fact]
    public async Task An_unlock_runs_out_unless_it_is_used()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/safe", SafePassword);

        App.Time.Advance(TimeSpan.FromMinutes(9));
        await AssertStatus(HttpStatusCode.OK, await GetWithCookieAsync(Users.Alice, "/safe/inside.md", cookie!));
        // Used just now, so it holds for another ten minutes from here
        App.Time.Advance(TimeSpan.FromMinutes(9));
        await AssertStatus(HttpStatusCode.OK, await GetWithCookieAsync(Users.Alice, "/safe/inside.md", cookie!));

        App.Time.Advance(TimeSpan.FromMinutes(11));
        await AssertPasswordRequired("/safe", await GetWithCookieAsync(Users.Alice, "/safe/inside.md", cookie!));
    }
}
