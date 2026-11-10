using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.History;

namespace notes.Tests.Unit;

/// <summary>Who the commits of the history are by.</summary>
public sealed class HistoryAuthorTests : HistoryTest
{
    [Fact]
    public void A_user_who_changes_what_another_one_changed_commits_it_first()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", "alice's\n");

        Save(history, Bob, "a.md", "bob's\n");

        Snapshot latest = Commits(history)[0];
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.Equal("#other-user", latest.Message);
        Assert.Equal("alice's\n", latest.Files["a.md"]);
    }

    [Fact]
    public void A_user_who_changes_their_own_changes_again_commits_nothing()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", "one\n");

        Save(history, Alice, "a.md", "two\n");

        Assert.Equal(2, Commits(history).Count);
    }

    [Fact]
    public void Changing_what_was_changed_outside_of_the_app_commits_that_first()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Notes.Files.File("a.md", "from outside\n");

        Save(history, Alice, "a.md", "alice's\n");

        Snapshot latest = Commits(history)[0];
        Assert.Equal(HistoryAuthor.App.Name, latest.Author);
        Assert.Equal("from outside\n", latest.Files["a.md"]);
    }

    [Fact]
    public void Changes_of_different_users_are_committed_by_each_of_them()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", "alice's\n");
        Save(history, Bob, "b.md", "bob's\n");

        history.CommitPending(CommitReason.Idle);

        List<Snapshot> commits = Commits(history);
        Assert.Equal(4, commits.Count);
        Snapshot byAlice = Assert.Single(commits, commit => commit.Author == Users.Alice.Name);
        Snapshot byBob = Assert.Single(commits, commit => commit.Author == Users.Bob.Name);
        // Alice's commit has only her change, Bob's comes on top of it
        Assert.False(byAlice.Files.ContainsKey("b.md"));
        Assert.Equal("alice's\n", byAlice.Files["a.md"]);
        Assert.Equal("bob's\n", byBob.Files["b.md"]);
        Assert.Equal("alice's\n", byBob.Files["a.md"]);
    }

    [Fact]
    public void Everything_in_a_directory_the_user_changed_is_theirs()
    {
        Notes.Files.File("docs/a.md", "a\n");
        Notes.Files.File("docs/b.md", "b\n");
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        TestDirectories.Delete(Notes.P("docs"));
        history.AfterChange(Alice, [Notes.P("docs")], HistoryChangeKind.Delete);
        history.CommitPending(CommitReason.Idle);

        Snapshot latest = Commits(history)[0];
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.DoesNotContain("docs/a.md", latest.Files.Keys);
    }

    [Fact]
    public void A_user_without_an_email_is_told_apart_by_their_id()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        ClaimsPrincipal withEmail = new(new ClaimsIdentity(
            [new Claim(NotesClaims.UserName, "dora"), new Claim(NotesClaims.UserId, "dora-id"), new Claim(NotesClaims.Email, "dora@example.com")], "Test"));

        Save(history, Alice, "a.md", "a\n");
        Save(history, withEmail, "b.md", "b\n");
        history.CommitPending(CommitReason.Idle);

        List<Snapshot> commits = Commits(history);
        Assert.Equal(Users.Alice.Sub, Assert.Single(commits, commit => commit.Author == Users.Alice.Name).Email);
        Assert.Equal("dora@example.com", Assert.Single(commits, commit => commit.Author == "dora").Email);
    }
}
