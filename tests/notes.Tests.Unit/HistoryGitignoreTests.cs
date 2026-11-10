using notes.Features.History;

namespace notes.Tests.Unit;

/// <summary>What the history leaves out (gitignore), and what happens when that changes.</summary>
public sealed class HistoryGitignoreTests : HistoryTest
{
    [Fact]
    public void Gitignore_leaves_out_the_folder_and_what_is_below_unless_a_subfolder_says_otherwise()
    {
        Notes.Files.Config("media", Cfg.Folder(gitignore: true, Cfg.Perms()));
        Notes.Files.File("media/video.bin", [1, 2, 3]);
        Notes.Files.File("media/other/clip.md", "left out as well\n");
        Notes.Files.Config("media/keep", Cfg.Folder(gitignore: false, Cfg.Perms()));
        Notes.Files.File("media/keep/notes.md", "kept\n");
        HistoryService history = History();

        history.CommitPending(CommitReason.Startup);

        IReadOnlyDictionary<string, string> files = Commits(history)[0].Files;
        Assert.Contains("media/.config.json", files.Keys);
        Assert.Contains("media/keep/.config.json", files.Keys);
        Assert.Contains("media/keep/notes.md", files.Keys);
        Assert.DoesNotContain("media/video.bin", files.Keys);
        Assert.DoesNotContain("media/other/clip.md", files.Keys);
    }

    [Fact]
    public void Files_that_gitignore_leaves_out_later_go_from_the_history_but_stay_on_disk()
    {
        Notes.Files.Config("media", Cfg.Folder(Cfg.Perms()));
        Notes.Files.File("media/a.md", "a\n");
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Assert.Contains("media/a.md", Commits(history)[0].Files.Keys);

        Notes.Files.Config("media", Cfg.Folder(gitignore: true, Cfg.Perms()));
        history.CommitPending(CommitReason.Idle);

        IReadOnlyDictionary<string, string> files = Commits(history)[0].Files;
        Assert.DoesNotContain("media/a.md", files.Keys);
        Assert.Contains("\"gitignore\": true", files["media/.config.json"]);
        Assert.True(Notes.Files.Exists("media/a.md"));
    }

    /// <summary>Saves a folder's configuration as the configuration editor does.</summary>

    [Fact]
    public void Before_gitignore_leaves_files_out_what_is_pending_in_them_is_committed_by_whoever_changed_it()
    {
        Notes.Files.Config("media", Cfg.Folder(Cfg.Perms()));
        Notes.Files.File("media/a.md", "a\n");
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "media/a.md", "edited by alice\n");

        SaveConfig(history, Admin, "media", Cfg.Folder(gitignore: true, Cfg.Perms()));

        List<Snapshot> commits = Commits(history);
        Assert.Equal(4, commits.Count);
        Assert.Equal("#gitignore", commits[0].Message);
        Assert.Equal(Users.Admin.Name, commits[0].Author);
        Assert.DoesNotContain("media/a.md", commits[0].Files.Keys);
        Assert.Contains("\"gitignore\": true", commits[0].Files["media/.config.json"]);
        Assert.Equal("#before-gitignore", commits[1].Message);
        Assert.Equal(Users.Alice.Name, commits[1].Author);
        Assert.Equal("edited by alice\n", commits[1].Files["media/a.md"]);
    }

    [Fact]
    public void Keeping_a_folder_again_brings_its_files_back_with_the_change()
    {
        Notes.Files.Config("media", Cfg.Folder(gitignore: true, Cfg.Perms()));
        Notes.Files.File("media/a.md", "a\n");
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        SaveConfig(history, Admin, "media", Cfg.Folder(gitignore: false, Cfg.Perms()));

        List<Snapshot> commits = Commits(history);
        // Nothing was pending: no commit before it
        Assert.Equal(3, commits.Count);
        Assert.Equal("#gitignore", commits[0].Message);
        Assert.Equal(Users.Admin.Name, commits[0].Author);
        Assert.Equal("a\n", commits[0].Files["media/a.md"]);
    }

    [Fact]
    public void Leaving_out_the_root_is_by_whoever_did_it()
    {
        Notes.Files.File("a.md", "a\n");
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);

        SaveConfig(history, Admin, "", Cfg.Folder(gitignore: true, Cfg.Perms()));

        Snapshot latest = Commits(history)[0];
        Assert.Equal("#gitignore", latest.Message);
        Assert.Equal(Users.Admin.Name, latest.Author);
        Assert.Equal([".config.json"], latest.Files.Keys);
    }

    [Fact]
    public void A_configuration_that_keeps_gitignore_as_it_was_commits_nothing()
    {
        Notes.Files.Config("media", Cfg.Folder(Cfg.Perms()));
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", "pending\n");

        SaveConfig(history, Admin, "media", Cfg.Folder(Cfg.Perms(inherit: false)));

        Assert.Equal(2, Commits(history).Count);
    }

    [Fact]
    public void A_save_of_a_file_that_is_left_out_never_reaches_the_threshold()
    {
        Notes.Files.Config("media", Cfg.Folder(gitignore: true, Cfg.Perms()));
        HistoryService history = History();
        history.CommitPending(CommitReason.Startup);
        Save(history, Alice, "a.md", "pending\n");

        Save(history, Alice, "media/big.md", Lines(100));

        Assert.Equal(2, Commits(history).Count);
    }
}
