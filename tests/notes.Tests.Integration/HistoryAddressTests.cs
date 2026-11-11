using System.Net;
using notes.Features.HistoryViewing;

namespace notes.Tests.Integration;

/// <summary>What an address with <c>?before=</c> answers when it can't be shown, and that nothing of an earlier state can be changed.</summary>
public sealed class HistoryAddressTests(NotesAppFactory app) : HistoryStateTest(app)
{
    // ---- Not there

    [Fact]
    public async Task What_was_not_there_at_that_time_is_sent_to_now()
    {
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset before = CommitNow();
        _ = Tree.File("docs/new.md", "new");
        _ = CommitNow();

        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/new.md?" + At(before)), "/docs/new.md", EarlierStateNotice.DidNotExistThen);
        // Before the first state there is nothing at all
        AssertLeft(await Client.GetAsync(Users.Admin, "/readme.md?" + At(Start.AddDays(-1))), "/readme.md", EarlierStateNotice.DidNotExistThen);
    }

    // ---- Hidden, left out, invalid

    [Fact]
    public async Task What_is_hidden_now_answers_like_a_missing_path()
    {
        SetRoot(historyAllow: ["l"]);
        DateTimeOffset then = CommitNow();

        foreach (string hidden in BaseTree.HiddenFromAlice)
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, $"/{hidden}?{At(then)}"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/.config.json?" + At(then)));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/.config.json?" + At(then)));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/_/history?" + At(then)));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/../outside?" + At(then)));
    }

    [Fact]
    public async Task What_the_history_leaves_out_now_has_no_history_and_only_admins_are_told_why()
    {
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset kept = CommitNow();
        Tree.Config("docs", Cfg.Folder(gitignore: true, Cfg.Perms()));
        _ = CommitNow();

        AssertLeft(await Client.GetAsync(Users.Admin, "/docs/a.md?" + At(kept)), "/docs/a.md", EarlierStateNotice.NotKeptInHistory);
        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(kept)), "/docs/a.md", EarlierStateNotice.NotPermitted);
        AssertLeft(await Client.GetAsync(Users.Admin, "/docs?" + At(kept)), "/docs", EarlierStateNotice.NotKeptInHistory);

        // Not listed in the state of the directory above either
        string root = await BodyOf(await Client.GetAsync(Users.Alice, "/?" + At(kept)));
        Assert.DoesNotContain(">docs<", root);
        Assert.Contains(">readme.md<", root);
    }

    [Fact]
    public async Task A_configuration_of_a_state_is_read_as_it_was_then()
    {
        // Written by an older version, before there were history lists: it gives the history to nobody
        Tree.Config("docs", """{ "version": "1.1.0", "perms": { "inherit": true, "rwAllow": ["g:team"] }, "gitignore": false, "files": [] }""");
        Tree.Config("team", "{ not valid");
        DateTimeOffset old = CommitNow();
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(historyAllow: ["g:team"])));
        Tree.Config("team", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], historyAllow: ["g:team"])));
        DateTimeOffset current = CommitNow();

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(current)));
        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(old)), "/docs/a.md", EarlierStateNotice.NotPermitted);
        AssertLeft(await Client.GetAsync(Users.Alice, "/team/plan.md?" + At(old)), "/team/plan.md", EarlierStateNotice.NotPermitted);
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Admin, "/team/plan.md?" + At(old)));
    }

    [Fact]
    public async Task Only_admins_are_told_that_there_is_no_history()
    {
        TestDirectories.Delete(History.GitDirectory);

        AssertLeft(await Client.GetAsync(Users.Admin, "/docs/a.md?" + At(Start)), "/docs/a.md", EarlierStateNotice.NoHistory);
        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(Start)), "/docs/a.md", EarlierStateNotice.NotPermitted);
        AssertLeft(await Client.GetAsync(Users.Anonymous, "/docs/a.md?" + At(Start)), "/docs/a.md", EarlierStateNotice.NotPermitted);
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/docs/a.md?history"));
    }

    [Theory]
    [InlineData("nonsense", EarlierStateNotice.InvalidTime)]
    [InlineData("2026-01-01T10:00:00", EarlierStateNotice.InvalidTime)]
    [InlineData("2026-01-01", EarlierStateNotice.InvalidTime)]
    [InlineData("", EarlierStateNotice.InvalidTime)]
    [InlineData("2999-01-01T00:00:00Z", EarlierStateNotice.FutureTime)]
    public async Task A_time_that_is_not_one_or_lies_ahead_is_sent_to_now(string time, string notice)
        => AssertLeft(await Client.GetAsync(Users.Admin, "/docs/a.md?edit&before=" + time), "/docs/a.md?edit", notice);

    [Fact]
    public async Task The_page_it_was_sent_to_says_why_once()
    {
        HttpResponseMessage page = await Client.SendAsync(Users.Admin, HttpMethod.Get, "/docs/a.md", null);
        Assert.DoesNotContain("notify(\"", await BodyOf(page));

        using HttpRequestMessage request = new(HttpMethod.Get, "/docs/a.md");
        request.Headers.Add(TestUser.HeaderName, Users.Admin.ToHeader());
        request.Headers.Add("Cookie", ".notes.history-notice=" + EarlierStateNotice.DidNotExistThen);
        HttpResponseMessage withNotice = await App.CreateClient().SendAsync(request, Cancellation);
        string body = await BodyOf(withNotice);
        Assert.Matches(@"notify\(""[^""]+"", 'danger'\)", body);
        Assert.Contains(".notes.history-notice=;", string.Join("\n", withNotice.Headers.GetValues("Set-Cookie")));
    }

    // ---- Nothing can be changed

    [Fact]
    public async Task Every_change_with_before_is_refused_also_for_admins()
    {
        string before = At(Start);
        foreach (TestUser user in new[] { Users.Admin, Users.Alice })
        {
            await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(user, "/docs/a.md?" + before, "changed"));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.DeleteAsync(user, "/docs/a.md?" + before));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(user, "/docs?create=file&" + before));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(user, "/docs/a.md?rename=b.md&" + before));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(user, "/docs?config&" + before, "{}"));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(user, "/?selection=add&" + before, new { paths = new[] { "docs/a.md" } }));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(user, "/docs?config&" + before));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(user, "/?selection&" + before));
        }

        Assert.Equal("a", Tree.Read("docs/a.md"));
        Assert.False(Tree.Exists("docs/b.md"));
        // The same answer for what doesn't exist or is hidden: it tells nothing about it
        await AssertStatus(HttpStatusCode.BadRequest, await Client.DeleteAsync(Users.Alice, "/vault/secret.md?" + before));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.DeleteAsync(Users.Alice, "/missing.md?" + before));
        Assert.True(Tree.Exists("vault/secret.md"));
    }
}
