using System.Security.Claims;
using Notes.Features.Bridges;
using Notes.Features.Configuration;

namespace Notes.Features.History;

/// <summary>
/// Keeps the history in git repositories of their own (<see cref="HistoryRepository"/>), so that what was changed can be found
/// again (earlier states are read by <see cref="HistoryViewing.EarlierStateReader"/>). Each location (the root, and each bridge
/// whose history is enabled) has its own, in its <c>_/history</c>, as data of a directory stays with it (<see cref="RepositoryHistory"/>).
/// Changes are committed, by the users who made them, when
/// <list type="bullet">
/// <item>nothing was changed in the app for <see cref="HistoryConfig.IdleMinutes"/> (<see cref="CommitIfIdle"/>, run by a background job),</item>
/// <item>the changed lines reach <see cref="HistoryConfig.LineThreshold"/>,</item>
/// <item>a user changes something that another user (or something outside the app) has changed since the last commit,</item>
/// <item>a binary file that was committed is deleted (its lines don't count towards the threshold),</item>
/// <item>a configuration changes what the history keeps (<c>gitignore</c>, see <see cref="KeptFiles"/>): once before, so that nothing pending is
/// lost, and once with the change,</item>
/// <item>the app starts, and something changed while it didn't run.</item>
/// </list>
/// A save is decided on before the file is written: what was pending is committed on its own, without the save. Nothing that goes wrong here
/// fails what the user did: it is logged.
/// A change in the app only looks at the paths it changes; changes made outside the app elsewhere are found by the next commit, which looks
/// at all of the entries.
/// </summary>
/// <remarks>Everything goes through one lock: a repository can only be used by one thread at a time.</remarks>
public sealed class HistoryService : IDisposable
{
    private readonly HistoryConfig _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<HistoryService> _logger;
    private readonly EntryLocations _locations;
    private readonly FolderConfigStore _configs;

    /// <summary>Replaced as a whole when an archived folder's history opens or closes (under the lock), so that it can be read without it.</summary>
    private volatile IReadOnlyList<RepositoryHistory> _histories;

    private readonly Lock _lock = new();

    private DateTimeOffset _lastChange;

    public HistoryService(Config config, EntryLocations locations, FolderConfigStore configs, TimeProvider time, ILogger<HistoryService> logger)
    {
        _settings = config.History;
        _time = time;
        _logger = logger;
        _locations = locations;
        _configs = configs;
        _histories = [.. locations.All.Where(location => location.KeepsHistory)
            .Select(location => new RepositoryHistory(location, locations, config.History, configs, time, logger))];
        _lastChange = time.GetUtcNow();
    }

    /// <summary>
    /// An archived folder was unzipped (and mounted, <see cref="EntryLocations.MountArchive"/>): its history (in its <c>_/history</c>, which
    /// the archive held) is kept from now on, and what changed in it since it was last committed (nothing, normally) is committed.
    /// </summary>
    public void ArchiveUnzipped(EntryLocation archive)
    {
        if (!archive.KeepsHistory)
            return;
        Run(() =>
        {
            RepositoryHistory history = new(archive, _locations, _settings, _configs, _time, _logger);
            _histories = [.. _histories, history];
            history.Commit(CommitReason.Startup);
        });
    }

    /// <summary>
    /// An archived folder is about to be zipped: what is pending in it is committed, and its repository closed, so that the archive holds it as
    /// it is and nothing in it is open. Its history (and those of the folders unzipped inside it) is no longer kept until it is unzipped again.
    /// </summary>
    public void ArchiveZipping(EntryLocation archive)
        => Run(() =>
        {
            // (One unzipped inside it is in its unzipped copy, in its own _/temp/unzipped)
            List<RepositoryHistory> closing = [.. _histories.Where(history => history.Location.IsArchive
                && (string.Equals(history.Location.PhysicalPath, archive.PhysicalPath, StringComparison.OrdinalIgnoreCase)
                    || history.Location.PhysicalPath.StartsWith(archive.PhysicalPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))];
            _histories = [.. _histories.Except(closing)];
            // The innermost first, which are zipped first too
            foreach (RepositoryHistory history in Enumerable.Reverse(closing))
            {
                try
                {
                    history.Commit(CommitReason.Zipping);
                }
                finally
                {
                    history.Dispose();
                }
            }
        });

    /// <summary>Where the repository of the root is (physical). Its work tree is the root.</summary>
    public string GitDirectory => Path.Combine(_locations.Root.DataDirectory, "history");

    /// <summary>
    /// Whether the history keeps the file or directory (physical path), as the configuration says now (<c>gitignore</c>, and whether its
    /// location has a history).
    /// </summary>
    public bool IsKept(string physicalPath, bool isDirectory)
        => HistoryOf(physicalPath) is { } history && !_locations.IsHiddenByBridge(physicalPath) && history.IsKept(physicalPath, isDirectory);

    /// <summary>Commits whatever is pending, e.g. when the app starts.</summary>
    public void CommitPending(CommitReason reason)
        => Run(() => ForEach(_histories, history => history.Commit(reason)));

    /// <summary>Commits what is pending if nothing was changed in the app for <see cref="HistoryConfig.IdleMinutes"/>.</summary>
    public void CommitIfIdle()
        => Run(() =>
        {
            if (_time.GetUtcNow() - _lastChange >= TimeSpan.FromMinutes(_settings.IdleMinutes))
                ForEach(_histories, history => history.Commit(CommitReason.Idle));
        });

    /// <summary>
    /// To be called right before the user changes the entries: commits what is pending if someone else changed any of them since the last
    /// commit, or, for a save (<paramref name="newContents"/> of the one file), if the save would make the changes reach the threshold or
    /// change what the history keeps. Each repository decides for the paths in it.
    /// </summary>
    public void BeforeChange(ClaimsPrincipal user, IReadOnlyCollection<string> physicalPaths, string? newContents = null)
        => Run(() => ForEach(ByHistory(physicalPaths), group => group.History.BeforeChange(user, group.Paths, newContents)));

    /// <summary>
    /// To be called right after the user changed the entries (all paths that are gone or new, and the configuration files that changed with
    /// them).
    /// </summary>
    public void AfterChange(ClaimsPrincipal user, IReadOnlyCollection<string> physicalPaths, HistoryChangeKind kind)
        => Run(() =>
        {
            _lastChange = _time.GetUtcNow();
            ForEach(ByHistory(physicalPaths), group => group.History.AfterChange(user, group.Paths, kind));
        });

    public void Dispose()
    {
        lock (_lock)
            foreach (RepositoryHistory history in _histories)
                history.Dispose();
    }

    /// <summary>The history of the location the path is in, or null if that has none.</summary>
    private RepositoryHistory? HistoryOf(string physicalPath)
        => _locations.LocationOf(physicalPath) is { } location ? _histories.FirstOrDefault(history => history.Location == location) : null;

    /// <summary>The paths by the history of their location; those in a location without one are left out.</summary>
    private List<(RepositoryHistory History, List<string> Paths)> ByHistory(IEnumerable<string> physicalPaths)
        => [.. physicalPaths.Select(path => (History: HistoryOf(path), Path: path))
            .Where(pair => pair.History is not null)
            .GroupBy(pair => pair.History!)
            .Select(group => (group.Key, group.Select(pair => pair.Path).ToList()))];

    private void Run(Action action)
    {
        lock (_lock)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "The history could not be updated");
            }
        }
    }

    /// <summary>Does it for each, so that a repository that fails doesn't keep the others from their history.</summary>
    private void ForEach<T>(IEnumerable<T> items, Action<T> action)
    {
        foreach (T item in items)
        {
            try
            {
                action(item);
            }
            catch (Exception e)
            {
                // Whatever goes wrong with the history, what the user did must still happen
                _logger.LogError(e, "The history could not be updated");
            }
        }
    }
}
