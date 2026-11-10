using System.Security.Claims;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using notes.Features.History;

namespace notes.Tests.Unit;

/// <summary>A notes directory of its own with histories on it, and ways to save into them and to look at their commits.</summary>
public abstract class HistoryTest : IDisposable
{
    private readonly List<HistoryService> _services = [];

    protected static readonly ClaimsPrincipal Alice = Users.Alice.ToPrincipal();
    protected static readonly ClaimsPrincipal Bob = Users.Bob.ToPrincipal();
    protected static readonly ClaimsPrincipal Admin = Users.Admin.ToPrincipal();

    protected HistoryTest()
    {
        Notes.Files.Config("", Cfg.Folder(Cfg.Perms()));
    }

    protected TempNotes Notes { get; } = new();

    protected FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public void Dispose()
    {
        foreach (HistoryService service in _services)
            service.Dispose();
        Notes.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A history whose threshold is 10 lines, a removed line counting twice.</summary>
    protected HistoryService History(bool enabled = true)
    {
        Config config = new()
        {
            Dir = Notes.Root,
            Version = Notes.Config.Version,
            Auth = Notes.Config.Auth,
            History = new HistoryConfig { Enabled = enabled, IdleMinutes = 10, LineThreshold = 10, RemovedLineWeight = 2 }
        };
        HistoryService service = new(config, Notes.Store, Time, NullLogger<HistoryService>.Instance);
        _services.Add(service);
        return service;
    }

    protected sealed record Snapshot(string Author, string Email, string Message, IReadOnlyDictionary<string, string> Files);

    /// <summary>The commits, newest first, each with the text of every file it has.</summary>
    protected static List<Snapshot> Commits(HistoryService history)
    {
        using Repository repository = new(history.GitDirectory);
        // By parents, not by time: the clock of the test doesn't move between commits
        return [.. repository.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Topological }).Select(commit => new Snapshot(commit.Author.Name, commit.Author.Email, commit.MessageShort, FilesOf(commit.Tree, "")))];
    }

    private static Dictionary<string, string> FilesOf(Tree tree, string prefix)
    {
        Dictionary<string, string> files = [];
        foreach (TreeEntry entry in tree)
        {
            if (entry.Target is Tree subtree)
            {
                foreach ((string path, string text) in FilesOf(subtree, prefix + entry.Name + "/"))
                    files[path] = text;
            }
            else
                files[prefix + entry.Name] = ((Blob)entry.Target).GetContentText();
        }

        return files;
    }

    protected static string Lines(int count, string word = "line")
        => string.Concat(Enumerable.Range(1, count).Select(i => $"{word} {i}\n"));

    /// <summary>Saves the file as the editor does.</summary>
    protected void Save(HistoryService history, ClaimsPrincipal user, string virtualPath, string contents)
    {
        string physical = Notes.P(virtualPath);
        history.BeforeChange(user, [physical], contents);
        Notes.Files.File(virtualPath, contents);
        history.AfterChange(user, [physical], HistoryChangeKind.Save);
    }

    /// <summary>Saves a folder's configuration as the configuration editor does.</summary>
    protected void SaveConfig(HistoryService history, ClaimsPrincipal user, string folder, string text)
    {
        string physical = Path.Combine(Notes.P(folder), ".config.json");
        history.BeforeChange(user, [physical], text);
        Notes.Files.Config(folder, text);
        history.AfterChange(user, [physical], HistoryChangeKind.Save);
    }
}
