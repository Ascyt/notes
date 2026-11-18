using System.Net;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using notes.Features.History;

namespace notes.Tests.Integration;

/// <summary>The history of a bridge, which is kept in a repository of its own, in the bridge's physical directory (docs/administration/bridges.md).</summary>
public sealed class BridgeHistoryTests(BridgedNotesAppFactory app) : BridgeTest(app)
{
    private HistoryService History => App.Services.GetRequiredService<HistoryService>();

    /// <summary>The files of the last commit of the repository in the directory.</summary>
    private static List<string> CommittedFiles(string physicalDirectory)
    {
        using Repository repository = new(Path.Combine(physicalDirectory, "_", "history"));
        return [.. Walk(repository.Head.Tip.Tree, "").Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> Walk(Tree tree, string prefix)
        => tree.SelectMany(entry => entry.TargetType == TreeEntryTargetType.Tree
            ? Walk((Tree)entry.Target, prefix + entry.Name + "/")
            : [prefix + entry.Name]);

    [Fact]
    public async Task A_bridge_has_its_history_in_its_own_physical_directory()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/hdd/test/note.md", "changed"));
        History.CommitPending(CommitReason.Idle);

        Assert.Contains("test/note.md", CommittedFiles(App.HddDir));
        // The root's history has nothing of the bridge, nor of what it hides on disk
        Assert.DoesNotContain(CommittedFiles(App.NotesDir), path => path.StartsWith("hdd/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("readme.md", CommittedFiles(App.NotesDir));
    }

    [Fact]
    public void A_bridge_without_a_history_gets_no_repository()
    {
        History.CommitPending(CommitReason.Idle);

        Assert.False(Directory.Exists(Path.Combine(App.ArchiveDir, "_", "history")));
    }

    [Fact]
    public async Task A_move_out_of_a_bridge_is_in_both_histories()
    {
        History.CommitPending(CommitReason.Idle);

        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Alice, "move", "", "hdd/test/note.md"));
        History.CommitPending(CommitReason.Idle);

        Assert.DoesNotContain("test/note.md", CommittedFiles(App.HddDir));
        Assert.Contains("note.md", CommittedFiles(App.NotesDir));
    }
}
