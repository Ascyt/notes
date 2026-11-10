using notes.Features.History;

namespace notes.Tests.Unit;

/// <summary>When the history commits, and what goes into a commit.</summary>
public sealed class HistoryCommitTests : HistoryTest
{
    [Fact]
    public void The_notes_as_they_are_when_the_app_starts_are_committed_by_the_app()
    {
        Notes.Files.File("a.md", "hello\n");
        HistoryService history = History();

        history.CommitPending(CommitReason.Startup);

        List<Snapshot> commits = Commits(history);
        Assert.Equal(2, commits.Count);
        Assert.Equal("_", commits[0].Author);
        Assert.Equal("_", commits[0].Email);
        Assert.Equal("#startup", commits[0].Message);
        Assert.Equal("hello\n", commits[0].Files["a.md"]);
        Assert.Contains(".config.json", commits[0].Files.Keys);
        // The history begins empty
        Assert.Empty(commits[1].Files);
        Assert.Equal("#init", commits[1].Message);
    }

    [Fact]
    public void Every_reason_has_a_keyword_of_its_own()
    {
        string[] keywords = [.. Enum.GetValues<CommitReason>().Select(CommitKeywords.Of).Append(CommitKeywords.Init)];

        Assert.All(keywords, keyword => Assert.Matches("^#[a-z]+(-[a-z]+)*$", keyword));
        Assert.Equal(keywords.Length, keywords.Distinct().Count());
    }

    [Fact]
    public void Nothing_pending_makes_no_commit()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        history.CommitPending(CommitReason.Startup);

        Assert.Equal(2, Commits(history).Count);
    }

    [Fact]
    public void Changes_are_committed_once_nothing_changed_for_a_while()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", "draft\n");

        Time.Advance(TimeSpan.FromMinutes(9));
        history.CommitIfIdle();
        Assert.Equal(2, Commits(history).Count);

        Time.Advance(TimeSpan.FromMinutes(1));
        history.CommitIfIdle();
        Snapshot latest = Commits(history)[0];
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.Equal("#idle", latest.Message);
        Assert.Equal("draft\n", latest.Files["a.md"]);
    }

    [Fact]
    public void A_save_that_reaches_the_threshold_commits_what_was_there_before_it()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", Lines(5));
        Assert.Equal(2, Commits(history).Count);

        Save(history, Alice, "a.md", Lines(11));

        List<Snapshot> commits = Commits(history);
        Assert.Equal(3, commits.Count);
        Assert.Equal("#threshold", commits[0].Message);
        Assert.Equal(Lines(5), commits[0].Files["a.md"]);
        Assert.Equal(Lines(11), Notes.Files.Read("a.md"));
    }

    [Fact]
    public void Removed_lines_count_more_than_added_ones()
    {
        Notes.Files.File("a.md", Lines(20));
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        // 3 removed count 6, 2 more make 10
        Save(history, Alice, "a.md", Lines(17));
        Assert.Equal(2, Commits(history).Count);
        Save(history, Alice, "a.md", Lines(15));

        Assert.Equal(Lines(17), Commits(history)[0].Files["a.md"]);
    }

    [Fact]
    public void Deleting_a_binary_file_that_was_committed_commits_right_away()
    {
        Notes.Files.File("image.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x01]);
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        history.BeforeChange(Alice, [Notes.P("image.png")]);
        File.Delete(Notes.P("image.png"));
        history.AfterChange(Alice, [Notes.P("image.png")], HistoryChangeKind.Delete);

        Snapshot latest = Commits(history)[0];
        Assert.Equal(3, Commits(history).Count);
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.Equal("#binary-deleted", latest.Message);
        Assert.DoesNotContain("image.png", latest.Files.Keys);
    }

    [Fact]
    public void Deleting_a_short_text_file_waits()
    {
        Notes.Files.File("a.md", "short\n");
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        File.Delete(Notes.P("a.md"));
        history.AfterChange(Alice, [Notes.P("a.md")], HistoryChangeKind.Delete);

        Assert.Equal(2, Commits(history).Count);
    }

    [Fact]
    public void An_operation_that_reaches_the_threshold_commits_with_it()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        // An upload, say: there is no telling before what it brings
        Notes.Files.File("big.md", Lines(30));
        history.AfterChange(Alice, [Notes.P("big.md")], HistoryChangeKind.Change);

        Snapshot latest = Commits(history)[0];
        Assert.Equal(Users.Alice.Name, latest.Author);
        Assert.Equal(Lines(30), latest.Files["big.md"]);
    }
}
