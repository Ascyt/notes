using System.Net;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using notes.Features.History;

namespace notes.Tests.Integration;

/// <summary>What the endpoints that change the notes put into the history (when it commits is tested on its own, in the unit tests).</summary>
public sealed class HistoryTests : IntegrationTest
{
    public HistoryTests(NotesAppFactory app) : base(app)
    {
        // The notes of the last test were removed with their history: what is there now is the start
        History.CommitPending(CommitReason.Startup);
    }

    private HistoryService History => App.Services.GetRequiredService<HistoryService>();

    private void RunIdleJob()
    {
        App.Time.Advance(TimeSpan.FromMinutes(App.Services.GetRequiredService<Config>().History.IdleMinutes));
        using IServiceScope scope = App.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CommitIdleChangesJob>().Run();
    }

    private sealed record Snapshot(string Author, string Message, IReadOnlyCollection<string> Paths, Func<string, string?> Read);

    /// <summary>The latest commit, or one before it.</summary>
    private Snapshot Latest(int before = 0)
    {
        using Repository repository = new(History.GitDirectory);
        // By parents, not by time: the clock of the test doesn't move between commits
        Commit commit = repository.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Topological }).Skip(before).First();
        Dictionary<string, string> files = [];
        Collect(commit.Tree, "", files);
        return new Snapshot(commit.Author.Name, commit.MessageShort, files.Keys, path => files.GetValueOrDefault(path));
    }

    private int CommitCount()
    {
        using Repository repository = new(History.GitDirectory);
        return repository.Commits.Count();
    }

    private static void Collect(Tree tree, string prefix, Dictionary<string, string> files)
    {
        foreach (TreeEntry entry in tree)
        {
            if (entry.Target is Tree subtree)
                Collect(subtree, prefix + entry.Name + "/", files);
            else
                files[prefix + entry.Name] = ((Blob)entry.Target).GetContentText();
        }
    }

    [Fact]
    public async Task A_save_is_committed_by_its_user_once_nothing_changed_for_a_while()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md", "by alice"));

        RunIdleJob();

        Snapshot latest = Latest();
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.Equal("by alice", latest.Read("docs/a.md"));
    }

    [Fact]
    public async Task A_save_of_what_another_user_changed_commits_their_change_first()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md", "by alice"));

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Carol, "/docs/a.md", "by carol"));

        Snapshot latest = Latest();
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.Equal("by alice", latest.Read("docs/a.md"));
        Assert.Equal("by carol", Tree.Read("docs/a.md"));
    }

    [Fact]
    public async Task Deleting_a_binary_file_that_is_in_the_history_commits_right_away()
    {
        _ = Tree.File("docs/image.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);
        History.CommitPending(CommitReason.Startup);
        int before = CommitCount();

        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Alice, "/docs/image.png"));

        Assert.Equal(before + 1, CommitCount());
        Snapshot latest = Latest();
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.DoesNotContain("docs/image.png", latest.Paths);
    }

    [Fact]
    public async Task A_folder_whose_configuration_sets_gitignore_leaves_the_history_at_once_after_what_was_pending_in_it()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md", "by alice"));

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs?config", Cfg.Folder(gitignore: true, Cfg.Perms())));

        Snapshot change = Latest();
        Assert.Equal("#gitignore", change.Message);
        Assert.Equal(Users.Admin.Name, change.Author);
        Assert.Contains("docs/.config.json", change.Paths);
        Assert.DoesNotContain("docs/a.md", change.Paths);
        Assert.DoesNotContain("docs/nested/b.md", change.Paths);
        Assert.True(Tree.Exists("docs/a.md"));

        Snapshot pending = Latest(before: 1);
        Assert.Equal("#before-gitignore", pending.Message);
        Assert.Equal(Users.Alice.Name, pending.Author);
        Assert.Equal("by alice", pending.Read("docs/a.md"));
    }

    [Fact]
    public async Task Renaming_and_creating_are_committed_by_whoever_did_it()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md?rename=renamed.md"));
        await AssertStatus(HttpStatusCode.Redirect, await Client.PostAsync(Users.Alice, "/docs?create=file"));

        RunIdleJob();

        Snapshot latest = Latest();
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.Contains("docs/renamed.md", latest.Paths);
        Assert.DoesNotContain("docs/a.md", latest.Paths);
    }

    [Theory]
    [InlineData("/_/history")]
    [InlineData("/_/history/HEAD")]
    [InlineData("/_/history/config")]
    public async Task The_repository_is_not_reachable_for_anyone(string url)
    {
        foreach (TestUser user in new[] { Users.Admin, Users.Alice, Users.Anonymous })
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(user, url));
    }
}
