using System.Text;
using Microsoft.Extensions.Configuration;
using Notes.Features.History;

namespace Notes.Tests.Unit;

/// <summary>Files larger than <c>History:MaxFileMegabytes</c> are left out of the history; null keeps every file.</summary>
public sealed class HistoryFileSizeTests : HistoryTest
{
    /// <summary>A kilobyte, as the largest file the tests allow.</summary>
    private const double OneKilobyte = 1.0 / 1024;

    private static string Text(int bytes) => new('x', bytes);

    [Fact]
    public void A_file_larger_than_the_limit_is_left_out_and_smaller_ones_are_kept()
    {
        Notes.Files.File("media/big.txt", Text(2048));
        Notes.Files.File("media/small.txt", Text(512));
        HistoryService history = History(maxFileMegabytes: OneKilobyte);

        history.CommitPending(CommitReason.Startup);

        IReadOnlyDictionary<string, string> files = Commits(history)[0].Files;
        Assert.Contains("media/small.txt", files.Keys);
        Assert.DoesNotContain("media/big.txt", files.Keys);
        Assert.False(history.IsKept(Notes.P("media/big.txt"), isDirectory: false));
        Assert.True(history.IsKept(Notes.P("media/small.txt"), isDirectory: false));
    }

    [Fact]
    public void A_file_that_grows_past_the_limit_leaves_the_history_and_its_earlier_version_stays()
    {
        Notes.Files.File("media/growing.txt", "small\n");
        HistoryService history = History(maxFileMegabytes: OneKilobyte);
        history.CommitPending(CommitReason.Startup);
        Assert.Equal("small\n", Commits(history)[0].Files["media/growing.txt"]);

        Notes.Files.File("media/growing.txt", Text(4096));
        history.CommitPending(CommitReason.Idle);

        List<Snapshot> commits = Commits(history);
        Assert.DoesNotContain("media/growing.txt", commits[0].Files.Keys);
        Assert.Equal("small\n", commits[1].Files["media/growing.txt"]);
        Assert.Equal(Text(4096), Notes.Files.Read("media/growing.txt"));
    }

    [Fact]
    public void Without_a_limit_every_file_is_kept()
    {
        Notes.Files.File("media/big.txt", Text(4096));
        HistoryService history = History(maxFileMegabytes: null);

        history.CommitPending(CommitReason.Startup);

        Assert.Contains("media/big.txt", Commits(history)[0].Files.Keys);
    }

    [Fact]
    public void A_configuration_is_kept_whatever_its_size()
    {
        string config = Cfg.Folder(Cfg.Perms());
        Notes.Files.Config("media", config.Insert(config.LastIndexOf('}'), new string(' ', 2048)));
        HistoryService history = History(maxFileMegabytes: OneKilobyte);

        history.CommitPending(CommitReason.Startup);

        Assert.Contains("media/.config.json", Commits(history)[0].Files.Keys);
    }

    private static HistoryConfig Bound(string json)
        => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build()
            .GetSection("History").Get<HistoryConfig>() ?? new HistoryConfig();

    [Fact]
    public void The_limit_is_50_megabytes_unless_set_and_null_in_appsettings_turns_it_off()
    {
        Assert.Equal(50, Bound("""{ "History": { "Enabled": true } }""").MaxFileMegabytes);
        Assert.Equal(2.5, Bound("""{ "History": { "MaxFileMegabytes": 2.5 } }""").MaxFileMegabytes);
        Assert.Null(Bound("""{ "History": { "MaxFileMegabytes": null } }""").MaxFileMegabytes);
    }
}
