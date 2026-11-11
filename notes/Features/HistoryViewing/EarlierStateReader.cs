using System.Collections.Concurrent;
using LibGit2Sharp;
using notes.Features.Configuration;
using notes.Features.Configuration.Migrations;
using notes.Features.History;

namespace notes.Features.HistoryViewing;

/// <summary>
/// Reads earlier states of the notes from the history (<see cref="EarlierState"/>). Each reader opens the repository on its own: reading
/// what is committed needs no lock, so it doesn't wait for <see cref="HistoryService"/>, which is the only one that writes.
/// </summary>
public sealed class EarlierStateReader(Config config, HistoryService history, ConfigMigrator migrator, ILogger<EarlierStateReader> logger)
{
    private readonly HistoryPaths _paths = new(config.Dir);

    /// <summary>What each configuration file of the history says, by the id of its contents: they never change, so neither does what they say.</summary>
    private readonly ConcurrentDictionary<ObjectId, FolderConfigState> _configurationFiles = new();

    /// <summary>Whether there is a history to read: it is turned on, and was started.</summary>
    public bool HasHistory => config.History.Enabled && Repository.IsValid(history.GitDirectory);

    /// <summary>The state before the time (the latest commit before it), or null if there is none. To be disposed.</summary>
    public EarlierState? OpenStateBefore(DateTimeOffset before)
    {
        if (!HasHistory)
            return null;

        Repository repository = new(history.GitDirectory);
        try
        {
            if (CommitsNewestFirst(repository).FirstOrDefault(commit => commit.Author.When < before) is { } found)
                return new EarlierState(repository, ownsRepository: true, found, _paths, ReadConfigurationFile);
        }
        catch
        {
            repository.Dispose();
            throw;
        }

        repository.Dispose();
        return null;
    }

    /// <summary>
    /// Every state there is, the newest first, each with the time it was replaced by the next one (null for the newest, which still is). Each
    /// state is only valid until the next one is asked for.
    /// </summary>
    public IEnumerable<(EarlierState State, DateTimeOffset? ReplacedAt)> EveryStateNewestFirst()
    {
        if (!HasHistory)
            yield break;

        using Repository repository = new(history.GitDirectory);
        DateTimeOffset? replacedAt = null;
        foreach (Commit commit in CommitsNewestFirst(repository))
        {
            using EarlierState state = new(repository, ownsRepository: false, commit, _paths, ReadConfigurationFile);
            yield return (state, replacedAt);
            // The next one (going back) was replaced by this one
            replacedAt = state.CommitTime;
        }
    }

    /// <summary>The history is a line: each commit has the one before it as its only parent.</summary>
    private static IEnumerable<Commit> CommitsNewestFirst(Repository repository)
    {
        for (Commit? commit = repository.Head.Tip; commit is not null; commit = commit.Parents.FirstOrDefault())
            yield return commit;
    }

    /// <summary>Read as the configuration on disk is (see <see cref="FolderConfigStore"/>): brought up to the current format, and never guessed at when it can't be read.</summary>
    private FolderConfigState ReadConfigurationFile(Blob blob)
        => _configurationFiles.GetOrAdd(blob.Id, _ =>
        {
            MigrationResult migrated = migrator.Migrate(EarlierState.ReadText(blob));
            if (migrated.Error is not null)
            {
                logger.LogDebug("A configuration file of the history can't be brought up to date: {error}", migrated.Error);
                return new FolderConfigState(null, [migrated.Error]);
            }

            ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(migrated.Text, existingFiles: null);
            return new FolderConfigState(parsed.Value, parsed.Errors);
        });
}
