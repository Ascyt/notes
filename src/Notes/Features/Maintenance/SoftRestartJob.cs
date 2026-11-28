using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;
using Notes.Features.ZippedFolders;

namespace Notes.Features.Maintenance;

/// <summary>
/// Does again what the app does at the start that is safe while it runs, for what was changed outside of it (configurations edited on the
/// server, files put there): what changed is committed to the history, configurations are brought up to date, and archived folders are turned
/// on and off as the configurations say now (also finding the <c>onLowestLevel</c> folders again). Never scheduled by default: started by hand
/// in the jobs' dashboard (see docs/administration/jobs.md).
/// </summary>
/// <remarks>
/// Not done again: what the start puts right after a crash in the work of zipping and unzipping (it may be in use now), and the checks of the
/// bridges, which stop the start when they fail. The configurations themselves are read again whenever their file changes, without this.
/// </remarks>
public sealed class SoftRestartJob(HistoryService history, FolderConfigStore configs, ArchivedFolders archives, StructureLock structureLock,
    ILogger<SoftRestartJob> logger)
{
    public const string Id = "soft-restart";

    public async Task RunAsync()
    {
        // (Nothing is moved by a user meanwhile; the pages go on working, operations wait for it)
        using (await structureLock.EnterAsync())
        {
            history.CommitPending(CommitReason.Startup);
            if (configs.MigrateAll() > 0)
                history.CommitPending(CommitReason.Migration);
            await archives.ReconcileEverywhereAsync();
        }
        logger.LogInformation("Soft restart done: history committed, configurations brought up to date, archived folders checked");
    }
}
