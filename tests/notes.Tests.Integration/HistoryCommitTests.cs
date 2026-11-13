using System.Net;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Requests;

namespace notes.Tests.Integration;

/// <summary>
/// The list of changes (<c>?commits</c>) and the state of a commit (<c>?commit=</c>): which commits a user learns of, from where the list
/// starts, and what the state of a commit shows.
/// </summary>
public sealed class HistoryCommitTests(NotesAppFactory app) : HistoryStateTest(app)
{
    private sealed record ListedCommit(string Id, string? Author);

    // A line of the list: its commit, and who made it (a name, or in italics what no user made in the app)
    private static readonly Regex LineOfList = new(@"data-commit=""(?<id>[0-9a-f]{40})""[^>]*>\s*<time[^>]*>[^<]*</time>\s*<(?<tag>span|i) class=""commit-author"">(?<author>[^<]*)<");

    private async Task<string> ListAsync(TestUser user, string address, int page = 1)
    {
        HttpResponseMessage response = await Client.GetAsync(user, address + (address.Contains('?') ? "&" : "?") + "commits&page=" + page);
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    /// <summary>
    /// The commits the list shows for the address (with its query, if any), and whether there are more: for a page older ones, for the newer
    /// ones (<c>&amp;newer</c>) newer ones still.
    /// </summary>
    private async Task<(List<ListedCommit> Commits, bool HasMore)> CommitsAsync(TestUser user, string address, int page = 1)
    {
        string body = await ListAsync(user, address, page);
        List<ListedCommit> commits = [.. LineOfList.Matches(body).Select(line => new ListedCommit(line.Groups["id"].Value, line.Groups["tag"].Value == "i" ? null : line.Groups["author"].Value))];
        bool hasMore = address.Contains("newer") ? HasShowNewer(body) : body.Contains("data-has-older=\"true\"");
        return (commits, hasMore);
    }

    private static bool HasShowNewer(string body) => body.Contains("id=\"commit-show-newer\"");

    private string NewestCommitId()
    {
        using Repository repository = new(History.GitDirectory);
        return repository.Head.Tip.Sha;
    }

    /// <summary>Saves the file as the user, through the app, so that the next commit is by them.</summary>
    private async Task SaveAsync(TestUser user, string path, string contents)
        => await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(user, "/" + path, contents));

    // ---- Which commits are listed

    [Fact]
    public async Task A_file_lists_the_commits_that_changed_it_newest_first_with_who_made_them()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();
        await SaveAsync(Users.Alice, "docs/a.md", "by alice");
        _ = CommitNow();
        string byAlice = NewestCommitId();
        _ = Tree.File("readme.md", "something else");
        _ = CommitNow();
        await SaveAsync(Users.Admin, "docs/a.md", "by admin");
        _ = CommitNow();
        string byAdmin = NewestCommitId();

        (List<ListedCommit> commits, bool hasOlder) = await CommitsAsync(Users.Alice, "/docs/a.md");

        // The first state (before the history was given) can't be seen by alice; the change of the configuration and of readme.md are no changes of the file
        Assert.Equal([byAdmin, byAlice], commits.Select(commit => commit.Id));
        Assert.Equal(["admin", "alice"], commits.Select(commit => commit.Author));
        Assert.False(hasOlder);

        // Admins also see the first state, which no user made in the app
        (List<ListedCommit> forAdmin, _) = await CommitsAsync(Users.Admin, "/docs/a.md");
        Assert.Equal(3, forAdmin.Count);
        Assert.Null(forAdmin[2].Author);
    }

    [Fact]
    public async Task A_directory_lists_the_changes_below_it_only_where_they_can_be_seen()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();
        _ = Tree.File("mixed/locked.md", "changed");
        _ = CommitNow();
        string unreadable = NewestCommitId();
        _ = Tree.File("mixed/closed/hidden.md", "changed");
        _ = CommitNow();
        string hidden = NewestCommitId();
        Tree.Config("mixed", Cfg.Folder(Cfg.Perms(historyDeny: ["u:nobody"]),
            ("locked.md", Cfg.Perms(inherit: false)), ("keep.md", Cfg.Perms(cdDeny: ["g:team"])), ("view.md", Cfg.Perms(rwReadOnly: ["g:team"]))));
        _ = CommitNow();
        string configurationOnly = NewestCommitId();
        _ = Tree.File("mixed/open.md", "changed");
        _ = CommitNow();
        string visible = NewestCommitId();
        File.Delete(Tree.Physical("mixed/view.md"));
        _ = CommitNow();
        string deleted = NewestCommitId();

        (List<ListedCommit> commits, _) = await CommitsAsync(Users.Alice, "/mixed");
        Assert.Equal([deleted, visible], commits.Select(commit => commit.Id));

        // Admins see every change of a file, but a change of the configuration only is still none
        (List<ListedCommit> forAdmin, _) = await CommitsAsync(Users.Admin, "/mixed");
        Assert.Contains(unreadable, forAdmin.Select(commit => commit.Id));
        Assert.Contains(hidden, forAdmin.Select(commit => commit.Id));
        Assert.DoesNotContain(configurationOnly, forAdmin.Select(commit => commit.Id));
    }

    [Fact]
    public async Task The_list_starts_at_the_state_that_is_shown_and_goes_back_in_pages()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();
        List<string> ids = [];
        List<DateTimeOffset> times = [];
        for (int i = 0; i < EarlierStateCommits.PageSize + 5; i++)
        {
            _ = Tree.File("docs/a.md", "version " + i);
            times.Add(CommitNow());
            ids.Add(NewestCommitId());
        }
        ids.Reverse();
        times.Reverse();

        (List<ListedCommit> first, bool firstHasOlder) = await CommitsAsync(Users.Alice, "/docs/a.md");
        (List<ListedCommit> second, bool secondHasOlder) = await CommitsAsync(Users.Alice, "/docs/a.md", page: 2);
        Assert.Equal(ids.Take(EarlierStateCommits.PageSize), first.Select(commit => commit.Id));
        Assert.True(firstHasOlder);
        Assert.Equal(ids.Skip(EarlierStateCommits.PageSize), second.Select(commit => commit.Id));
        Assert.False(secondHasOlder);

        // From a commit: it and what is older; from a time: what is before it
        (List<ListedCommit> fromCommit, _) = await CommitsAsync(Users.Alice, "/docs/a.md?commit=" + ids[3]);
        Assert.Equal(ids[3], fromCommit[0].Id);
        (List<ListedCommit> fromTime, _) = await CommitsAsync(Users.Alice, "/docs/a.md?" + At(times[3]));
        Assert.Equal(ids[3], fromTime[0].Id);
        (List<ListedCommit> fromNowhere, _) = await CommitsAsync(Users.Alice, "/docs/a.md?commit=" + new string('0', 40));
        Assert.Empty(fromNowhere);

        // The way to newer ones is only there where there are any
        Assert.True(HasShowNewer(await ListAsync(Users.Alice, "/docs/a.md?commit=" + ids[3])));
        Assert.True(HasShowNewer(await ListAsync(Users.Alice, "/docs/a.md?" + At(times[3]))));
        Assert.False(HasShowNewer(await ListAsync(Users.Alice, "/docs/a.md?commit=" + ids[0])));
        Assert.False(HasShowNewer(await ListAsync(Users.Alice, "/docs/a.md")));
        Assert.False(HasShowNewer(await ListAsync(Users.Alice, "/docs/a.md?commit=" + ids[3], page: 2)));

        // Newer ones: those right after the commit (or the state before a time), a page at most, and whether there are newer still
        (List<ListedCommit> newer, bool hasNewer) = await CommitsAsync(Users.Alice, "/docs/a.md?newer&commit=" + ids[3]);
        Assert.Equal(ids.Take(3), newer.Select(commit => commit.Id));
        Assert.False(hasNewer);
        (List<ListedCommit> newerThanTime, _) = await CommitsAsync(Users.Alice, "/docs/a.md?newer&" + At(times[3]));
        Assert.Equal(ids.Take(3), newerThanTime.Select(commit => commit.Id));
        (List<ListedCommit> newerThanOldest, bool hasNewerThanOldest) = await CommitsAsync(Users.Alice, "/docs/a.md?newer&commit=" + ids[^1]);
        Assert.Equal(ids.Skip(ids.Count - 1 - EarlierStateCommits.PageSize).Take(EarlierStateCommits.PageSize), newerThanOldest.Select(commit => commit.Id));
        Assert.True(hasNewerThanOldest);
        // Now has nothing newer
        (List<ListedCommit> newerThanNow, _) = await CommitsAsync(Users.Alice, "/docs/a.md?newer");
        Assert.Empty(newerThanNow);
    }

    [Fact]
    public async Task The_list_is_only_told_to_those_who_may_see_the_history_now()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Bob, "/readme.md?commits"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/readme.md?commits"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/secret.md?commits"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault?commits"));
        Tree.Config("docs", Cfg.Folder(gitignore: true, Cfg.Perms()));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/docs/a.md?commits"));
    }

    // ---- The state of a commit

    [Fact]
    public async Task A_commit_shows_the_entry_as_that_commit_left_it_and_who_made_it()
    {
        SetRoot(historyAllow: ["g:team"]);
        await SaveAsync(Users.Alice, "docs/a.md", "first version");
        _ = CommitNow();
        string first = NewestCommitId();
        await SaveAsync(Users.Alice, "docs/a.md", "second version");
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + first));
        Assert.Contains("first version", body);
        Assert.DoesNotContain("second version", body);
        Assert.Contains("id=\"history-card\"", body);
        Assert.Matches(@"id=""history-card-commit""[^>]*>[^<]*<span>alice</span>", body);
        // The list of changes is there from the start (without asking for it), with the commit marked, and its × leads back to now
        Assert.Matches(@"<aside id=""commit-sidebar""(?![^>]*hidden)[^>]*>", body);
        Assert.Matches($@"data-commit=""{first}""\s+aria-current=""true""", body);
        Assert.Matches(@"id=""commit-sidebar-exit""[^>]*href=""/docs/a.md""", body);
        // Elsewhere it is asked for when it is shown
        Assert.Matches(@"<aside id=""commit-sidebar""[^>]*hidden", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md")));

        // Ids are accepted in upper case too, as git does
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + first.ToUpperInvariant()));
    }

    [Fact]
    public async Task A_commit_that_changed_nothing_the_user_may_see_is_shown_without_what_it_was()
    {
        SetRoot(historyAllow: ["g:team", "u:carol"]);
        Tree.Config("vault", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["u:carol"], cdAllow: ["u:carol"], historyAllow: ["u:carol"])));
        _ = CommitNow();
        await SaveAsync(Users.Carol, "vault/secret.md", "changed by carol");
        _ = CommitNow();
        string secret = NewestCommitId();

        string forAlice = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + secret));
        Assert.Contains("id=\"history-card\"", forAlice);
        Assert.DoesNotContain("history-card-commit", forAlice);
        Assert.DoesNotContain("carol", forAlice);

        string forCarol = await BodyOf(await Client.GetAsync(Users.Carol, "/docs/a.md?commit=" + secret));
        Assert.Contains("history-card-commit", forCarol);
    }

    [Fact]
    public async Task Images_in_markdown_load_from_the_commit()
    {
        _ = Tree.File("docs/a.md", "![picture](picture.png)");
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();
        string id = NewestCommitId();

        Assert.Contains("picture.png?raw&amp;commit=" + id, await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + id)));
    }

    [Fact]
    public async Task A_commit_that_is_not_one_is_sent_to_now()
    {
        AssertLeft(await Client.GetAsync(Users.Admin, "/docs/a.md?edit&commit=nonsense"), "/docs/a.md?edit", EarlierStateNotice.InvalidCommit);
        AssertLeft(await Client.GetAsync(Users.Admin, "/docs/a.md?commit="), "/docs/a.md", EarlierStateNotice.InvalidCommit);
        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + new string('0', 40)), "/docs/a.md", EarlierStateNotice.NoSuchCommit);
        // What isn't there now (or is hidden) answers like a missing path, whatever the commit
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/missing.md?commit=" + new string('0', 40)));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/secret.md?commit=" + NewestCommitId()));
    }

    [Fact]
    public async Task A_commit_the_user_may_not_see_the_entry_at_is_sent_to_now()
    {
        // The first state is from before the history was given
        string before = NewestCommitId();
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();

        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + before), "/docs/a.md", EarlierStateNotice.NotPermitted);
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + NewestCommitId()));
    }

    [Fact]
    public async Task Every_change_with_a_commit_is_refused_also_for_admins()
    {
        string commit = "commit=" + NewestCommitId();
        foreach (TestUser user in new[] { Users.Admin, Users.Alice })
        {
            await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(user, "/docs/a.md?" + commit, "changed"));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.DeleteAsync(user, "/docs/a.md?" + commit));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(user, "/docs?create=file&" + commit));
            await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(user, "/docs?config&" + commit));
            // One state at a time
            await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(user, "/docs?" + commit + "&" + At(Start)));
        }

        Assert.Equal("a", Tree.Read("docs/a.md"));
    }
}
