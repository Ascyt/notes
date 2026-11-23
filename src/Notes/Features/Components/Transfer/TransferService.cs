using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.Passwords;
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
/// <remarks>
/// Passwords travel with what they protect, unlike permissions (<see cref="CarryPasswords"/>): otherwise copying something would be a way
/// around its password.
/// </remarks>
public sealed class TransferService(TransferTexts texts, TransferPlanner planner, TransferExecutor executor, StructureLock structureLock, FolderConfigStore configs,
    HistoryService history, PasswordLocks passwordLocks, EntryLocations locations, ILogger<TransferService> logger) : ITransferService
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
            // (Found before: a move takes the originals, and their entries, away)
            List<CarriedPassword> passwords = kind == TransferKind.Delete ? [] : PasswordsOf(physicalTarget, plan.Items);
            executor.Execute(kind, physicalTarget, plan.Items, reportProgress);
            KeepFileEntriesInStep(kind, physicalTarget, plan.Items);
            CarryPasswords(passwords);
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

    /// <summary>A password that goes with a transferred entry (or one inside it) to where it goes.</summary>
    private sealed record CarriedPassword(Protection From, string Destination, bool IsDirectory);

    /// <summary>
    /// The passwords of the transferred entries and of everything that is transferred inside them, with where each goes: the directories
    /// (outermost first) before the files, so a file's folder has its configuration first.
    /// </summary>
    private List<CarriedPassword> PasswordsOf(string physicalTarget, IEnumerable<TransferItem> items)
    {
        List<CarriedPassword> passwords = [];
        void Add(string source, string destination, bool isDirectory)
        {
            string? hash = isDirectory
                ? configs.Get(source).Config?.PasswordHash
                : configs.Get(locations.ParentOf(source)!).Config?.FindFile(Path.GetFileName(source))?.PasswordHash;
            if (hash is not null)
                passwords.Add(new CarriedPassword(new Protection(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)), hash), destination,
                    isDirectory));
        }

        foreach (TransferItem item in items)
        {
            string destination = Path.Combine(physicalTarget, item.Name);
            Add(item.Source, destination, item.IsDirectory);
            foreach (InsideEntry inside in item.Contents)
                Add(inside.Path, Path.Combine(destination, inside.RelativePath), inside.IsDirectory);
        }

        return [.. passwords.OrderBy(password => !password.IsDirectory)];
    }

    /// <summary>
    /// Gives each transferred entry the password it had. One that can't be given it is logged as an error: the entry is there without it.
    /// Whoever had one unlocked has it unlocked where it went.
    /// </summary>
    private void CarryPasswords(List<CarriedPassword> passwords)
    {
        foreach (CarriedPassword password in passwords)
        {
            bool carried = password.IsDirectory
                ? configs.DirectoryPasswordCarried(password.Destination, password.From.Hash)
                : configs.FilePasswordCarried(Path.GetDirectoryName(password.Destination)!, Path.GetFileName(password.Destination),
                    password.From.Hash);
            if (carried)
                passwordLocks.Moved(password.From, password.Destination);
            else
                logger.LogError("{destination} was transferred, but could not be given the password it had: it is not protected by it", password.Destination);
        }
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
