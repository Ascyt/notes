using notes.Features.History;

namespace notes.Tests.Unit;

/// <summary>Where the repository of the history is, and that it is there when it is needed.</summary>
public sealed class HistoryRepositoryTests : HistoryTest
{
    [Fact]
    public void The_repository_is_in_the_reserved_folder_and_nothing_of_it_is_among_the_notes()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        Assert.Equal(Path.Combine(Notes.Root, "_", "history"), history.GitDirectory);
        Assert.False(Path.Exists(Path.Combine(Notes.Root, ".git")));
        Assert.DoesNotContain(Commits(history)[0].Files.Keys, path => path.StartsWith("_/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_history_that_is_turned_off_makes_no_repository()
    {
        HistoryService history = History(enabled: false);

        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", Lines(50));

        Assert.False(Directory.Exists(history.GitDirectory));
    }

    [Fact]
    public void A_repository_that_was_removed_is_made_again()
    {
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        TestDirectories.Delete(history.GitDirectory);
        Notes.Files.File("a.md", "a\n");
        history.CommitPending(CommitReason.Startup);

        Assert.Equal("a\n", Commits(history)[0].Files["a.md"]);
    }
}
