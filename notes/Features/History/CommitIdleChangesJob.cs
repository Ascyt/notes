namespace notes.Features.History;

/// <summary>Commits what is pending once nothing was changed in the app for a while (see <see cref="HistoryService.CommitIfIdle"/>).</summary>
public sealed class CommitIdleChangesJob(HistoryService history)
{
    public const string Id = "commit-idle-changes";

    public void Run()
        => history.CommitIfIdle();
}
