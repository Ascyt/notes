using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.Components.Transfer;

public interface ITransferService
{
    /// <summary>
    /// Copies or moves the entries (virtual paths) into the directory, or deletes them (the directory is then only the one the user is
    /// looking at). All or nothing: everything is checked first, and if anything fails while doing it, what was done so far is undone.
    /// <paramref name="reportProgress"/> is told how much of it is done (0 to 1) as it goes.
    /// </summary>
    Task<TransferResult> RunAsync(ClaimsPrincipal user, TransferKind kind, string physicalTarget, IReadOnlyList<string> virtualPaths,
        Action<double>? reportProgress = null);
}

/// <summary>
/// Copies, moves and deletes files and directories as a transaction: the <see cref="TransferPlanner"/> checks everything without touching
/// anything, then the <see cref="TransferExecutor"/> does it and undoes it all if it fails. Both happen under the <see cref="StructureLock"/>,
/// so nothing can change in between and no other operation sees a transfer half done. Turns the outcome into a <see cref="TransferResult"/>.
/// </summary>
public sealed class TransferService(TransferTexts texts, TransferPlanner planner, TransferExecutor executor, StructureLock structureLock, FolderConfigStore configs,
    HistoryService history, ILogger<TransferService> logger) : ITransferService
{
    public async Task<TransferResult> RunAsync(ClaimsPrincipal user, TransferKind kind, string physicalTarget,
        IReadOnlyList<string> virtualPaths, Action<double>? reportProgress = null)
    {
        // Once started, a transfer is never abandoned half way because the request that asked for it went away
        using (await structureLock.EnterAsync())
            return await Task.Run(() => Run(user, kind, physicalTarget, virtualPaths, reportProgress ?? (_ => { })));
    }

    private TransferResult Run(ClaimsPrincipal user, TransferKind kind, string physicalTarget, IReadOnlyList<string> virtualPaths,
        Action<double> reportProgress)
    {
        try
        {
            TransferPlan plan = planner.Plan(user, kind, physicalTarget, virtualPaths);
            List<string> changed = ChangedPaths(kind, physicalTarget, plan.Items);
            history.BeforeChange(user, changed);
            executor.Execute(kind, physicalTarget, plan.Items, reportProgress);
            KeepFileEntriesInStep(kind, physicalTarget, plan.Items);
            history.AfterChange(user, changed, kind == TransferKind.Delete ? HistoryChangeKind.Delete : HistoryChangeKind.Change);
            return TransferResult.Success with { LeftOutUnreadable = plan.LeftOutUnreadable };
        }
        catch (TransferException e)
        {
            return new TransferResult(e.StatusCode, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The exception's message names paths on the server, which are for the log only
            logger.LogError(e, "{kind} into {target} failed", kind, physicalTarget);
            return new TransferResult(StatusCodes.Status500InternalServerError,
                texts.FailedAndUndone(kind switch { TransferKind.Copy => WhatIsDone.Copy, TransferKind.Move => WhatIsDone.Move, _ => WhatIsDone.Delete }));
        }
    }

    /// <summary>What the transfer changes: the entries that go away or come in, and the configurations whose file entries change with them.</summary>
    private static List<string> ChangedPaths(TransferKind kind, string physicalTarget, IEnumerable<TransferItem> items)
    {
        List<string> changed = [];
        foreach (TransferItem item in items)
        {
            if (kind != TransferKind.Copy)
                changed.Add(item.Source);
            if (kind != TransferKind.Delete)
                changed.Add(Path.Combine(physicalTarget, item.Name));
            if (!item.IsDirectory && kind != TransferKind.Copy)
                changed.Add(Path.Combine(Path.GetDirectoryName(item.Source)!, PathService.ConfigFileName));
            if (!item.IsDirectory && kind != TransferKind.Delete)
                changed.Add(Path.Combine(physicalTarget, PathService.ConfigFileName));
        }

        return [.. changed.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The files that were transferred get their entry in the permissions of the folder they went to (a new one that changes nothing: the
    /// permissions of a file don't travel), and the ones that went away lose theirs. Directories need nothing: the permissions of one are in
    /// the directory, and travel with it for those who may move them (see <see cref="AccessTree.Walk"/>).
    /// </summary>
    private void KeepFileEntriesInStep(TransferKind kind, string physicalTarget, IEnumerable<TransferItem> items)
    {
        foreach (TransferItem item in items.Where(item => !item.IsDirectory))
        {
            if (kind != TransferKind.Copy)
                configs.FileRemoved(Path.GetDirectoryName(item.Source)!, item.Name);
            if (kind != TransferKind.Delete)
                configs.FileAdded(physicalTarget, item.Name);
        }
    }
}
