using System.Text;
using LibGit2Sharp;
using notes.Features.Helpers;

namespace notes.Features.History;

/// <summary>How many lines of a file were added and removed since the last commit.</summary>
public readonly record struct LineChanges(string Path, int Added, int Removed);

/// <summary>
/// The git repository of the history: <c>_/history</c>, bare, with the notes as its work tree, made when it is first needed (also when it was
/// removed while the app runs). Stages what <see cref="KeptFiles"/> keeps, compares it with the last commit and commits it. When to commit is
/// for <see cref="HistoryService"/> to decide, under whose lock this is used: the repository can only be used by one thread at a time.
/// </summary>
public sealed class HistoryRepository(HistoryPaths paths, KeptFiles kept, TimeProvider time, ILogger logger) : IDisposable
{
    private static readonly StatusOptions WorkDirectoryStatus = new()
    {
        Show = StatusShowOption.WorkDirOnly,
        IncludeUntracked = true,
        RecurseUntrackedDirs = true,
        IncludeIgnored = false,
        ExcludeSubmodules = true,
        DetectRenamesInIndex = false,
        DetectRenamesInWorkDir = false
    };

    private Repository? _repository;

    /// <summary>Where the repository is (physical).</summary>
    public string GitDirectory { get; } = Path.Combine(paths.Root, PathService.ReservedName, "history");

    /// <summary>Brings the index up to the files on disk, without what is left out.</summary>
    public void Stage()
    {
        Repository repository = Open();
        Func<string, bool> isKept = kept.ForManyFiles();

        // Files that were kept before and are left out now (their folder's gitignore changed) are no longer kept
        foreach (string path in repository.Index.Select(entry => entry.Path).Where(path => !isKept(path)).ToList())
            repository.Index.Remove(path);

        string[] changed =
        [
            .. repository.RetrieveStatus(WorkDirectoryStatus)
                .Where(entry => entry.State is not (FileStatus.Unaltered or FileStatus.Ignored))
                .Select(entry => entry.FilePath)
                .Where(isKept)
        ];
        if (changed.Length > 0)
            Commands.Stage(repository, changed);
        repository.Index.Write();
    }

    /// <summary>What is staged and differs from the last commit (git paths).</summary>
    public List<string> PendingPaths()
    {
        Repository repository = Open();
        return [.. repository.Diff.Compare<TreeChanges>(repository.Head.Tip.Tree, DiffTargets.Index).Select(change => change.Path)];
    }

    /// <summary>The lines of what is staged that differ from the last commit.</summary>
    public List<LineChanges> PendingLines()
    {
        Repository repository = Open();
        return [.. repository.Diff.Compare<Patch>(repository.Head.Tip.Tree, DiffTargets.Index).Select(change => new LineChanges(change.Path, change.LinesAdded, change.LinesDeleted))];
    }

    /// <summary>How the file would differ from its last commit with the new contents.</summary>
    public LineChanges LinesOfSave(string path, string newContents)
    {
        Repository repository = Open();
        // The editor writes UTF-8 without a byte order mark (see SaveHandler)
        using MemoryStream contents = new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(newContents));
        Blob saved = repository.ObjectDatabase.CreateBlob(contents);
        Blob committed = repository.Head.Tip[path]?.Target as Blob ?? repository.ObjectDatabase.CreateBlob(Stream.Null);
        ContentChanges changes = repository.Diff.Compare(committed, saved);
        return new LineChanges(path, changes.LinesAdded, changes.LinesDeleted);
    }

    /// <summary>Whether any of the paths, which are gone from disk, is or holds a binary file of the last commit.</summary>
    public bool DeletesCommittedBinary(IEnumerable<string> gitPaths)
    {
        Repository repository = Open();
        return gitPaths.Where(path => !Path.Exists(paths.ToPhysicalPath(path)))
            .Select(path => repository.Head.Tip[path]?.Target)
            .Any(IsOrHoldsBinary);
    }

    /// <summary>
    /// Stages and commits what is pending. Changes by different authors go into one commit each, so that each is by whoever made it.
    /// </summary>
    /// <returns>How many paths were committed.</returns>
    public int Commit(string message, Func<string, HistoryAuthor> authorOf)
    {
        Stage();
        List<string> pending = PendingPaths();
        if (pending.Count == 0)
            return 0;

        Repository repository = Open();
        Tree head = repository.Head.Tip.Tree;
        Dictionary<string, IndexEntry?> staged = pending.ToDictionary(path => path, path => (IndexEntry?)repository.Index[path], StringComparer.Ordinal);
        List<IGrouping<HistoryAuthor, string>> byAuthor = [.. pending.GroupBy(authorOf).OrderBy(group => group.Key.Name, StringComparer.Ordinal)];

        // All but the first author's changes wait in the state of the last commit, and come in one author at a time
        foreach (string path in byAuthor.Skip(1).SelectMany(group => group))
            SetIndexEntry(repository, path, head[path] is { TargetType: TreeEntryTargetType.Blob } entry ? (entry.Target.Id, entry.Mode) : null);

        for (int i = 0; i < byAuthor.Count; i++)
        {
            if (i > 0)
            {
                foreach (string path in byAuthor[i])
                    SetIndexEntry(repository, path, staged[path] is { } entry ? (entry.Id, entry.Mode) : null);
            }

            repository.Index.Write();
            Signature signature = Signature(byAuthor[i].Key);
            _ = repository.Commit(message, signature, signature, new CommitOptions());
        }

        return pending.Count;
    }

    public void Dispose()
    {
        _repository?.Dispose();
        _repository = null;
    }

    private Repository Open()
    {
        if (_repository is not null && Directory.Exists(GitDirectory))
            return _repository;

        Dispose();
        bool isNew = !Repository.IsValid(GitDirectory);
        if (isNew)
        {
            // Bare, so that nothing (no .git) is put among the notes; the work tree is given when it is opened
            _ = Repository.Init(GitDirectory, isBare: true);
            _ = Directory.CreateDirectory(Path.Combine(GitDirectory, "info"));
            File.WriteAllText(Path.Combine(GitDirectory, "info", "exclude"), $"/{PathService.ReservedName}/\n");
        }

        Repository repository = new(GitDirectory, new RepositoryOptions { WorkingDirectoryPath = paths.Root, IndexPath = Path.Combine(GitDirectory, "index") });
        if (isNew)
        {
            // So that git itself finds the notes too (git --git-dir=_/history log), wherever they are moved
            repository.Config.Set("core.bare", false);
            repository.Config.Set("core.worktree", "../..");
            repository.Config.Set("core.autocrlf", false);
            Signature signature = Signature(HistoryAuthor.App);
            _ = repository.Commit(CommitKeywords.Init, signature, signature, new CommitOptions { AllowEmptyCommit = true });
            logger.LogInformation("Started the history of the notes in {directory}", GitDirectory);
        }

        _repository = repository;
        return repository;
    }

    private static void SetIndexEntry(Repository repository, string path, (ObjectId Id, Mode Mode)? entry)
    {
        if (entry is { } blob)
            repository.Index.Add(repository.Lookup<Blob>(blob.Id), path, blob.Mode);
        else
            repository.Index.Remove(path);
    }

    private Signature Signature(HistoryAuthor author)
        => new(author.Name, author.Email, time.GetLocalNow());

    private static bool IsOrHoldsBinary(GitObject? target)
        => target switch
        {
            Blob blob => blob.IsBinary,
            Tree tree => tree.Any(entry => IsOrHoldsBinary(entry.Target)),
            _ => false
        };
}
