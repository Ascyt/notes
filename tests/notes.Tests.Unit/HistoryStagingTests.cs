using notes.Features.History;

namespace notes.Tests.Unit;

/// <summary>That a change in the app only looks at what it changes, and that the next commit still finds everything.</summary>
public sealed class HistoryStagingTests : HistoryTest
{
    [Fact]
    public void Changes_outside_the_app_elsewhere_count_from_the_next_commit_not_with_a_save()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Notes.Files.File("outside.md", Lines(30));

        Save(history, Alice, "a.md", "one line\n");

        // The save didn't look at the other file, so its lines didn't reach the threshold
        Assert.Equal(2, Commits(history).Count);

        Time.Advance(TimeSpan.FromMinutes(10));
        history.CommitIfIdle();
        Snapshot latest = Commits(history)[0];
        Assert.Equal(Lines(30), latest.Files["outside.md"]);
        Assert.Equal("one line\n", latest.Files["a.md"]);
    }

    [Fact]
    public void A_new_directory_brings_everything_in_it()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        // A copy, say: the directory stands for everything in it
        Notes.Files.File("copy/a.md", Lines(6));
        Notes.Files.File("copy/deeper/b.md", Lines(6));
        history.AfterChange(Alice, [Notes.P("copy")], HistoryChangeKind.Change);

        Snapshot latest = Commits(history)[0];
        Assert.Equal("#threshold", latest.Message);
        Assert.Equal(Lines(6), latest.Files["copy/a.md"]);
        Assert.Equal(Lines(6), latest.Files["copy/deeper/b.md"]);
    }

    [Fact]
    public void A_deleted_directory_takes_everything_in_it_along()
    {
        Notes.Files.File("old/a.md", Lines(6));
        Notes.Files.File("old/deeper/b.md", Lines(6));
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        history.BeforeChange(Alice, [Notes.P("old")]);
        TestDirectories.Delete(Notes.P("old"));
        history.AfterChange(Alice, [Notes.P("old")], HistoryChangeKind.Delete);

        // 12 removed lines, counting twice
        Snapshot latest = Commits(history)[0];
        Assert.Equal("#threshold", latest.Message);
        Assert.DoesNotContain(latest.Files.Keys, path => path.StartsWith("old/", StringComparison.Ordinal));
    }

    [Fact]
    public void Names_that_look_like_patterns_are_taken_as_they_are()
    {
        Notes.Files.File("[a]/x.md", "x\n");
        Notes.Files.File("a/x.md", "a\n");
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Notes.Files.File("a/x.md", Lines(30));

        Save(history, Alice, "[a]/x.md", "saved\n");

        // "[a]" as a pattern would have meant "a" as well, whose lines would have reached the threshold
        Assert.Equal(2, Commits(history).Count);
    }
}
