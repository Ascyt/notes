using System.Security.Claims;
using notes.Features.Configuration;

namespace notes.Features.History;

/// <summary>
/// Keeps the history of the notes in a git repository of their own (<see cref="HistoryRepository"/>), so that what was changed can be found
/// again (earlier states are read by <see cref="HistoryViewing.EarlierStateReader"/>). This decides when to commit: changes are committed, by the users who made them, when
/// <list type="bullet">
/// <item>nothing was changed in the app for <see cref="HistoryConfig.IdleMinutes"/> (<see cref="CommitIfIdle"/>, run by a background job),</item>
/// <item>the changed lines reach <see cref="HistoryConfig.LineThreshold"/>,</item>
/// <item>a user changes something that another user (or something outside the app) has changed since the last commit,</item>
/// <item>a binary file that was committed is deleted (its lines don't count towards the threshold),</item>
/// <item>a configuration changes what the history keeps (<c>gitignore</c>, see <see cref="KeptFiles"/>): once before, so that nothing pending is lost, and once with the change,</item>
/// <item>the app starts, and something changed while it didn't run.</item>
/// </list>
/// A save is decided on before the file is written: what was pending is committed on its own, without the save. Nothing that goes wrong here
/// fails what the user did: it is logged.
/// A change in the app only looks at the paths it changes; changes made outside the app elsewhere are found by the next commit, which looks
/// at all of the notes.
/// </summary>
/// <remarks>Everything goes through one lock: the repository can only be used by one thread at a time.</remarks>
public sealed class HistoryService : IDisposable
{
    private readonly HistoryConfig _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<HistoryService> _logger;
    private readonly HistoryPaths _paths;
    private readonly KeptFiles _kept;
    private readonly HistoryRepository _repository;
    private readonly HistoryEditors _editors = new();
    private readonly Lock _lock = new();

    private DateTimeOffset _lastChange;

    /// <summary>The configuration file (git path) about to be saved with another <c>gitignore</c>, between <see cref="BeforeChange"/> and <see cref="AfterChange"/>.</summary>
    private string? _gitignoreChange;

    public HistoryService(Config config, FolderConfigStore configs, TimeProvider time, ILogger<HistoryService> logger)
    {
        _settings = config.History;
        _time = time;
        _logger = logger;
        _paths = new HistoryPaths(config.Dir);
        _kept = new KeptFiles(configs, _paths);
        _repository = new HistoryRepository(_paths, _kept, time, logger);
        _lastChange = time.GetUtcNow();
    }

    /// <summary>Where the repository is (physical). Its work tree is the root of the notes.</summary>
    public string GitDirectory => _repository.GitDirectory;

    /// <summary>Whether the history keeps the file or directory (physical path), as the configuration says now (<c>gitignore</c>).</summary>
    public bool IsKept(string physicalPath, bool isDirectory)
        => _kept.IsEntryKept(physicalPath, isDirectory);

    /// <summary>Commits whatever is pending, e.g. when the app starts.</summary>
    public void CommitPending(CommitReason reason)
        => Run(() => Commit(reason));

    /// <summary>Commits what is pending if nothing was changed in the app for <see cref="HistoryConfig.IdleMinutes"/>.</summary>
    public void CommitIfIdle()
        => Run(() =>
        {
            if (_time.GetUtcNow() - _lastChange >= TimeSpan.FromMinutes(_settings.IdleMinutes))
                Commit(CommitReason.Idle);
        });

    /// <summary>
    /// To be called right before the user changes the entries: commits what is pending if someone else changed any of them since the last
    /// commit, or, for a save (<paramref name="newContents"/> of the one file), if the save would make the changes reach the threshold or
    /// change what the history keeps.
    /// </summary>
    public void BeforeChange(ClaimsPrincipal user, IReadOnlyCollection<string> physicalPaths, string? newContents = null)
        => Run(() =>
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
        });

    /// <summary>To be called right after the user changed the entries (all paths that are gone or new, and the configuration files that changed with them).</summary>
    public void AfterChange(ClaimsPrincipal user, IReadOnlyCollection<string> physicalPaths, HistoryChangeKind kind)
        => Run(() =>
        {
            HistoryAuthor author = HistoryAuthor.Of(user);
            List<string> affected = [.. physicalPaths.Select(_paths.ToGitPath)];
            foreach (string path in affected)
                _editors.Record(path, author);
            _lastChange = _time.GetUtcNow();

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
        });

    public void Dispose()
    {
        lock (_lock)
            _repository.Dispose();
    }

    private void Run(Action action)
    {
        if (!_settings.Enabled)
            return;

        lock (_lock)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                // Whatever goes wrong with the history, what the user did must still happen
                _logger.LogError(e, "The history of the notes could not be updated");
            }
        }
    }

    private void Commit(CommitReason reason)
    {
        int count = _repository.Commit(CommitKeywords.Of(reason), _editors.AuthorOf);
        if (count == 0)
            return;

        _editors.Clear();
        _logger.LogInformation("Committed {count} changed path(s) to the history ({reason})", count, reason);
    }

    private double PendingWeight(string? except)
        => _repository.PendingLines()
            .Where(lines => except is null || !string.Equals(lines.Path, except, StringComparison.OrdinalIgnoreCase))
            .Sum(Weight);

    private double Weight(LineChanges lines)
        => lines.Added + lines.Removed * _settings.RemovedLineWeight;
}
