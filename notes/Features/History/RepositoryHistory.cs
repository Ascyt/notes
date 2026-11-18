using System.Security.Claims;
using notes.Features.Bridges;
using notes.Features.Configuration;

namespace notes.Features.History;

/// <summary>
/// The history of one location of the notes (the root, or a bridge), in a repository in its own <c>_/history</c>: decides, for the paths in
/// it, when to commit, as <see cref="HistoryService"/> describes. Only used under the lock of <see cref="HistoryService"/>.
/// </summary>
public sealed class RepositoryHistory : IDisposable
{
    private readonly HistoryConfig _settings;
    private readonly ILogger _logger;
    private readonly HistoryPaths _paths;
    private readonly KeptFiles _kept;
    private readonly HistoryRepository _repository;
    private readonly HistoryEditors _editors = new();

    /// <summary>The configuration file (git path) about to be saved with another <c>gitignore</c>, between <see cref="BeforeChange"/> and <see cref="AfterChange"/>.</summary>
    private string? _gitignoreChange;

    public RepositoryHistory(NotesLocation location, NotesLocations locations, HistoryConfig settings, FolderConfigStore configs, TimeProvider time, ILogger logger)
    {
        Location = location;
        _settings = settings;
        _logger = logger;
        _paths = new HistoryPaths(location.PhysicalPath);
        _kept = new KeptFiles(configs, _paths, locations);
        _repository = new HistoryRepository(_paths, _kept, time, logger);
    }

    public NotesLocation Location { get; }

    /// <summary>Where the repository is (physical). Its work tree is the physical directory of the location.</summary>
    public string GitDirectory => _repository.GitDirectory;

    /// <summary>Whether the history keeps the file or directory (physical path, in this location), as the configuration says now.</summary>
    public bool IsKept(string physicalPath, bool isDirectory)
        => _kept.IsEntryKept(physicalPath, isDirectory);

    /// <inheritdoc cref="HistoryService.BeforeChange"/>
    public void BeforeChange(ClaimsPrincipal user, IReadOnlyCollection<string> physicalPaths, string? newContents)
    {
        // (One that was never written, as saving it failed, is forgotten)
        _gitignoreChange = null;
        List<string> affected = [.. physicalPaths.Select(_paths.ToGitPath)];
        // Only what is about to change needs to be up to date: whether someone else changed it, and what the save adds to it
        _repository.Stage(affected);
        List<string> pending = _repository.PendingPaths();

        // What the history keeps is about to change: what is pending goes in first, as it is now (by whoever changed it), so that nothing
        // of it is lost when its files are left out
        if (newContents is not null && affected.Count == 1 && _kept.ChangesWhatIsKept(affected[0], newContents))
        {
            _gitignoreChange = affected[0];
            Commit(CommitReason.BeforeGitignore);
            return;
        }

        if (pending.Count == 0)
            return;

        HistoryAuthor author = HistoryAuthor.Of(user);
        if (pending.Any(path => affected.Any(changed => HistoryPaths.IsAtOrBelow(path, changed)) && _editors.AuthorOf(path) != author))
        {
            Commit(CommitReason.OtherUser);
            return;
        }

        if (newContents is null || affected.Count != 1 || !_kept.IsKept(affected[0]))
            return;

        double weight = PendingWeight(except: affected[0]) + Weight(_repository.LinesOfSave(affected[0], newContents));
        if (weight >= _settings.LineThreshold)
            Commit(CommitReason.Threshold);
    }

    /// <inheritdoc cref="HistoryService.AfterChange"/>
    public void AfterChange(ClaimsPrincipal user, IReadOnlyCollection<string> physicalPaths, HistoryChangeKind kind)
    {
        HistoryAuthor author = HistoryAuthor.Of(user);
        List<string> affected = [.. physicalPaths.Select(_paths.ToGitPath)];
        foreach (string path in affected)
            _editors.Record(path, author);

        // The configuration that changes what the history keeps is in place: the change, and the files that leave or come back with it,
        // are committed at once, by whoever made it
        if (kind == HistoryChangeKind.Save && _gitignoreChange is { } configuration && affected.Contains(configuration, StringComparer.OrdinalIgnoreCase))
        {
            _gitignoreChange = null;
            _editors.Record(HistoryPaths.FolderOf(configuration)!, author);
            Commit(CommitReason.Gitignore);
            return;
        }

        // A save was decided on before it was written
        if (kind == HistoryChangeKind.Save)
            return;

        if (kind == HistoryChangeKind.Delete && _repository.DeletesCommittedBinary(affected))
        {
            Commit(CommitReason.BinaryDeleted);
            return;
        }

        _repository.Stage(affected);
        if (PendingWeight(except: null) >= _settings.LineThreshold)
            Commit(CommitReason.Threshold);
    }

    public void Commit(CommitReason reason)
    {
        int count = _repository.Commit(CommitKeywords.Of(reason), _editors.AuthorOf);
        if (count == 0)
            return;

        _editors.Clear();
        _logger.LogInformation("Committed {count} changed path(s) to the history of {location} ({reason})", count, Location.IsRoot ? "the notes" : "/" + Location.VirtualPath, reason);
    }

    public void Dispose()
        => _repository.Dispose();

    private double PendingWeight(string? except)
        => _repository.PendingLines()
            .Where(lines => except is null || !string.Equals(lines.Path, except, StringComparison.OrdinalIgnoreCase))
            .Sum(Weight);

    private double Weight(LineChanges lines)
        => lines.Added + lines.Removed * _settings.RemovedLineWeight;
}
