using System.Net;
using notes.Features.HistoryViewing.Requests;

namespace notes.Tests.Integration;

/// <summary>Who may see which earlier state: Read and history, now and then, of the entry and the directories above it.</summary>
public sealed class HistoryStateTests(NotesAppFactory app) : HistoryStateTest(app)
{
    // ---- Seeing a state

    [Fact]
    public async Task An_entry_open_at_that_time_is_shown_as_it_was_read_only()
    {
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset granted = CommitNow();
        _ = Tree.File("readme.md", "# Changed later");
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/readme.md?" + At(granted)));

        Assert.Contains("Readme", body);
        Assert.DoesNotContain("Changed later", body);
        Assert.Contains("id=\"history-card\"", body);
        Assert.Contains("id=\"history-button\"", body);
        // Nothing that changes anything, though alice may change the file now: only the ways to its source
        Assert.DoesNotContain("id=\"editor\"", body);
        Assert.DoesNotContain("id=\"edit-button\"", body);
        Assert.DoesNotContain("id=\"edit-here-button\"", body);
        Assert.Contains("id=\"view-source-button\"", body);
        Assert.Contains("id=\"view-source-here-button\"", body);
        Assert.DoesNotContain("id=\"delete-button\"", body);
        Assert.DoesNotContain("id=\"config-button\"", body);
    }

    [Fact]
    public async Task Edit_shows_the_source_of_an_entry_as_it_was_read_only()
    {
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset granted = CommitNow();
        _ = Tree.File("readme.md", "# Changed later");
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/readme.md?edit&" + At(granted)));

        Assert.Contains("id=\"editor\"", body);
        Assert.Contains("\"canWrite\":false", body);
        Assert.Contains("Readme", body);
        Assert.DoesNotContain("Changed later", body);
        Assert.Contains("id=\"history-card\"", body);
        Assert.DoesNotContain("id=\"markdown-preview\"", body);
        Assert.DoesNotContain("id=\"save-button\"", body);
        Assert.DoesNotContain("id=\"delete-button\"", body);
    }

    [Fact]
    public async Task A_directory_is_listed_as_it_was_without_ways_to_change_it()
    {
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset then = CommitNow();
        _ = Tree.File("docs/later.md", "later");
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs?" + At(then)));

        Assert.Contains(">a.md<", body);
        Assert.DoesNotContain("later.md", body);
        Assert.DoesNotContain("id=\"upload-button\"", body);
        Assert.DoesNotContain("id=\"delete-button\"", body);
        Assert.DoesNotContain("class=\"form-check-input entry-select", body);
        Assert.DoesNotContain("selection-bar", body);
        Assert.Matches(@"data-disabled-reason=""[^""]+""", body); // (the name can't be changed, and says why)
    }

    [Fact]
    public async Task Admins_see_every_state_of_what_is_kept()
    {
        _ = Tree.File("vault/secret.md", "changed");
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Admin, "/vault/secret.md?" + At(Start)));

        Assert.Contains("the secret", body);
    }

    // ---- Who may see a state: Read and history, now and then

    [Fact]
    public async Task Without_the_history_now_nothing_of_it_can_be_seen()
    {
        DateTimeOffset withoutHistory = Start;
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset granted = CommitNow();
        SetRoot();
        _ = CommitNow();

        // Given then, but not now
        AssertLeft(await Client.GetAsync(Users.Alice, "/readme.md?" + At(granted)), "/readme.md", EarlierStateNotice.NotPermitted);
        AssertLeft(await Client.GetAsync(Users.Alice, "/readme.md?" + At(withoutHistory)), "/readme.md", EarlierStateNotice.NotPermitted);
        AssertLeft(await Client.GetAsync(Users.Anonymous, "/readme.md?" + At(granted)), "/readme.md", EarlierStateNotice.NotPermitted);
    }

    [Fact]
    public async Task Given_now_it_only_opens_the_times_it_was_given_then_as_well()
    {
        DateTimeOffset before = Start;
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset granted = CommitNow();
        SetRoot();
        DateTimeOffset taken = CommitNow();
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/readme.md?" + At(granted)));
        AssertLeft(await Client.GetAsync(Users.Alice, "/readme.md?" + At(taken)), "/readme.md", EarlierStateNotice.NotPermitted);
        AssertLeft(await Client.GetAsync(Users.Alice, "/readme.md?" + At(before)), "/readme.md", EarlierStateNotice.NotPermitted);
    }

    [Fact]
    public async Task Read_comes_first_now_and_then()
    {
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset readable = CommitNow();
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("a.md", Cfg.Perms(rwDeny: ["u:alice"]))));
        DateTimeOffset unreadable = CommitNow();
        Tree.Config("docs", Cfg.Folder(Cfg.Perms()));
        _ = CommitNow();

        // A gap in Read doesn't end the history: the times on both sides of it can be seen, the gap itself can't
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(readable)));
        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(unreadable)), "/docs/a.md", EarlierStateNotice.NotPermitted);
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/nested/b.md?" + At(unreadable)));

        // Not readable now: answered as an unreadable entry now, whatever it was then
        Tree.Config("mixed", Cfg.Folder(Cfg.Perms(historyAllow: ["g:team"]), ("locked.md", Cfg.Perms(inherit: false, historyAllow: ["g:team"]))));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Alice, "/mixed/locked.md?" + At(readable)));
    }

    [Fact]
    public async Task A_directory_above_that_could_not_be_read_then_closes_what_is_in_it()
    {
        SetRoot(historyAllow: ["g:team"]);
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(rwDeny: ["u:alice"]), ("a.md", Cfg.Perms(rwAllow: ["u:alice"]))));
        DateTimeOffset closed = CommitNow();
        Tree.Config("docs", Cfg.Folder(Cfg.Perms()));
        _ = CommitNow();

        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(closed)), "/docs/a.md", EarlierStateNotice.NotPermitted);
    }

    [Fact]
    public async Task The_history_of_an_entry_does_not_need_the_history_of_the_directories_above()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(historyAllow: ["g:team"])));
        DateTimeOffset granted = CommitNow();

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(granted)));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs?" + At(granted)));
        // (The root's own listing is its history, which alice doesn't have)
        AssertLeft(await Client.GetAsync(Users.Alice, "/?" + At(granted)), "/", EarlierStateNotice.NotPermitted);
    }

    [Fact]
    public async Task The_history_may_be_given_to_those_who_are_not_logged_in()
    {
        SetRoot(historyExistingOnly: ["a"]);
        DateTimeOffset granted = CommitNow();

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, "/docs/a.md?" + At(granted)));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, "/?" + At(granted)));
    }

    // ---- What was deleted since

    [Fact]
    public async Task What_was_deleted_since_can_only_be_seen_with_history_allow()
    {
        SetRoot(historyAllow: ["g:team"], historyExistingOnly: ["g:guests"]);
        DateTimeOffset existed = CommitNow();
        File.Delete(Tree.Physical("docs/a.md"));
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(existed)));
        Assert.Contains("id=\"history-card\"", body);
        // Only what exists now: what is gone answers like a path that never was
        HttpResponseMessage gone = await Client.GetAsync(Users.Bob, "/docs/a.md?" + At(existed));
        HttpResponseMessage missing = await Client.GetAsync(Users.Bob, "/docs/never.md?" + At(existed));
        await AssertStatus(HttpStatusCode.NotFound, gone);
        await AssertStatus(HttpStatusCode.NotFound, missing);
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Bob, "/docs/nested/b.md?" + At(existed)));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Nobody, "/docs/a.md?" + At(existed)));
    }
}
