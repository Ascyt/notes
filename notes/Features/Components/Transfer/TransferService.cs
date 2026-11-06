using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Helpers;

namespace notes.Features.Components.Transfer;

public interface ITransferService
{
    /// <summary>
    /// Copies or moves the entries (virtual paths) into the directory, or deletes them (the directory is then only the one the user is
    /// looking at). All or nothing: everything is checked first, and if anything fails while doing it, what was done so far is undone.
    /// </summary>
    Task<TransferResult> RunAsync(ClaimsPrincipal user, TransferKind kind, string physicalTarget, IReadOnlyList<string> virtualPaths);
}

/// <summary>
/// Copies, moves and deletes files and directories as a transaction: the <see cref="TransferPlanner"/> checks everything without touching
/// anything, then the <see cref="TransferExecutor"/> does it and undoes it all if it fails. Both happen under the <see cref="StructureLock"/>,
/// so nothing can change in between and no other operation sees a transfer half done. Turns the outcome into a <see cref="TransferResult"/>.
/// </summary>
public sealed class TransferService(TransferPlanner planner, TransferExecutor executor, StructureLock structureLock, ILogger<TransferService> logger) : ITransferService
{
    public async Task<TransferResult> RunAsync(ClaimsPrincipal user, TransferKind kind, string physicalTarget, IReadOnlyList<string> virtualPaths)
    {
        // TODO: Remove this once proper testing is implemented
        // Lock out all non-admin user from these operations until we can be sure there are no security issues here 
        if (!user.IsInRole(NotesAccess.AdminRole)) 
            return new TransferResult(StatusCodes.Status401Unauthorized, "Unauthorized");
        
        // Once started, a transfer is never abandoned half way because the request that asked for it went away
        using (await structureLock.EnterAsync())
            return await Task.Run(() => Run(user, kind, physicalTarget, virtualPaths));
    }

    private TransferResult Run(ClaimsPrincipal user, TransferKind kind, string physicalTarget, IReadOnlyList<string> virtualPaths)
    {
        try
        {
            TransferPlan plan = planner.Plan(user, kind, physicalTarget, virtualPaths);
            executor.Execute(kind, physicalTarget, plan.Items);
            return TransferResult.Success with { LeftOutUnreadable = plan.LeftOutUnreadable };
        }
        catch (TransferException e)
        {
            return new TransferResult(e.StatusCode, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "{kind} into {target} failed", kind, physicalTarget);
            return new TransferResult(StatusCodes.Status500InternalServerError, $"The {kind.ToString().ToLowerInvariant()} failed and was undone: {e.Message}");
        }
    }
}
