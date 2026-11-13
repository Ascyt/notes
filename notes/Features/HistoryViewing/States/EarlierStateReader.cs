using System.Collections.Concurrent;
using LibGit2Sharp;
using notes.Features.Configuration;
using notes.Features.Configuration.Migrations;
using notes.Features.History;

namespace notes.Features.HistoryViewing.States;

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
        => OpenNewestStateThat(commit => commit.Author.When < before);

    /// <summary>The state the commit with the id made (the full hash, in lower case), or null if the history has none. To be disposed.</summary>
    public EarlierState? OpenStateOfCommit(string id)
        => OpenNewestStateThat(commit => commit.Sha == id);

    /// <summary>
    /// Every state from the one of the commit with the id (or the newest) back to the first, each with the state before it (null for the
    /// first). Each pair is only valid until the next one is asked for. Nothing if the history has no such commit.
    /// </summary>
    public IEnumerable<(EarlierState State, EarlierState? Previous)> EveryStateWithPreviousNewestFirst(string? fromCommitId)
    {
        if (!HasHistory)
            yield break;

        using Repository repository = new(history.GitDirectory);
        Commit? start = fromCommitId is null ? repository.Head.Tip : CommitsNewestFirst(repository).FirstOrDefault(commit => commit.Sha == fromCommitId);
        EarlierState? state = start is null ? null : new EarlierState(repository, ownsRepository: false, start, _paths, ReadConfigurationFile);
        // Each state's previous one is the next state: it is opened once, and what it has looked up is kept
        while (state is not null)
        {
            EarlierState? previous = state.OpenPrevious();
            yield return (state, previous);
            state = previous;
        }
    }

    private EarlierState? OpenNewestStateThat(Func<Commit, bool> isTheOne)
    {
        if (!HasHistory)
            return null;

        Repository repository = new(history.GitDirectory);
        try
        {
            if (CommitsNewestFirst(repository).FirstOrDefault(isTheOne) is { } found)
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
