using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.EntryActions;
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
    HistoryService history, PasswordLocks passwordLocks, EntryLocations locations, IPathService paths, ZippedFolders.ArchivedFolders archives,
    AccessTree tree, EntryTexts entryTexts, ILogger<TransferService> logger) : ITransferService
{
    public async Task<TransferResult> RunAsync(ClaimsPrincipal user, TransferKind kind, string physicalTarget,
        IReadOnlyList<string> virtualPaths, Action<double>? reportProgress = null)
    {
        Action<double> report = reportProgress ?? (_ => { });
        // Once started, a transfer is never abandoned half way because the request that asked for it went away
        using (await structureLock.EnterAsync())
        {
            // The target may have been made an archived folder while this waited: then it is in its unzipped copy now
            if (kind != TransferKind.Delete)
                physicalTarget = archives.WhereItIsNow(physicalTarget);

            // What is in the archived folders it takes is checked as for any directory: they are unzipped for that (those the user may
            // open), and stay so while it is checked
            List<string> sources = [.. virtualPaths.Select(paths.ToPhysicalPath).OfType<string>()];
            try
            {
                await archives.UnzipAllAtOrBelowAsync(sources, folder => tree.CanOpen(user, folder) && passwordLocks.FirstLocked(folder) is null);
            }
            catch (IOException e)
            {
                logger.LogError(e, "An archived folder of a {kind} into {target} could not be unzipped", kind, physicalTarget);
                return new TransferResult(StatusCodes.Status500InternalServerError, texts.ArchivedFolderUnzipFailed);
            }

            TransferPlan plan;
            using (await archives.UseAllAroundAsync(sources))
            {
                try
                {
                    plan = await Task.Run(() => planner.Plan(user, kind, physicalTarget, virtualPaths));
                }
                catch (TransferException e)
                {
                    return new TransferResult(e.StatusCode, e.Message);
                }

                // A copy copies what is in them, as for any directory: the copy is a normal one (archiving is a setting of the folder)
                if (kind == TransferKind.Copy)
                {
                    TransferResult copied = await Task.Run(() => Run(user, kind, physicalTarget, plan, report));
                    // Below an onLowestLevel folder, what holds files now is archived right away
                    await archives.ArchiveLowestLevelAfterChangeAsync(physicalTarget);
                    return copied;
                }
            }

            // A move or delete takes them as their folders (archive and configuration), zipped first, so a moved one stays archived
            try
            {
                TransferResult result = await archives.WhileZippedAsync([.. plan.Items.Select(item => item.Source)], folders =>
                    Task.Run(() => Run(user, kind, physicalTarget,
                        plan with { Items = [.. plan.Items.Select((item, i) => AsZipped(item, folders[i]))] }, report)));

                // One archived by an onLowestLevel folder above that was moved out from under it is an ordinary folder again where it is now
                // (and one moved under such a folder may be archived by it)
                if (kind == TransferKind.Move)
                {
                    foreach (TransferItem item in plan.Items.Where(item => item.IsDirectory))
                        _ = await archives.ReconcileAsync(Path.Combine(physicalTarget, item.Name));
                    await archives.ArchiveLowestLevelAfterChangeAsync(physicalTarget);
                }
                return result;
            }
            catch (IOException e)
            {
                logger.LogWarning(e, "The archived folders of a {kind} into {target} could not be zipped first", kind, physicalTarget);
                return new TransferResult(StatusCodes.Status409Conflict, entryTexts.InUse);
            }
        }
    }

    /// <summary>
    /// The item as it is once the archived folders at or in it are zipped (<paramref name="source"/>: the folder, for one that was its unzipped
    /// copy): what the plan found in each of them (in its copy) is now in its archive, so each goes as its configuration and archive. Those go
    /// for everyone, unlike other configurations: without its own, an archive would be a file like any other, which hands out what is in it.
    /// </summary>
    private TransferItem AsZipped(TransferItem item, string source)
    {
        if (!item.IsDirectory)
            return item with { Source = source };

        List<string> archived = [.. archives.ArchivedAtOrBelow([source])
            .Select(folder => Path.GetRelativePath(source, folder)).Select(relative => relative == "." ? "" : relative)];
        if (archived.Count == 0)
            return item with { Source = source };

        bool IsInsideArchived(InsideEntry entry)
            => archived.Any(relative => relative == "" || entry.RelativePath.StartsWith(relative + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        List<InsideEntry> contents = [.. item.Contents.Where(entry => !IsInsideArchived(entry))];
        foreach (string relative in archived)
        {
            string folder = Path.Combine(source, relative);
            contents.Add(new InsideEntry(Path.Combine(folder, PathService.ConfigFileName), Path.Combine(relative, PathService.ConfigFileName), false));
            if (ZippedFolders.ArchivedFolders.ArchiveFileOf(folder) is { } archive)
                contents.Add(new InsideEntry(archive, Path.Combine(relative, Path.GetFileName(archive)), false));
        }
        return item with { Source = source, Contents = contents };
    }

    private TransferResult Run(ClaimsPrincipal user, TransferKind kind, string physicalTarget, TransferPlan plan, Action<double> reportProgress)
    {
        try
        {
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
    private List<string> ChangedPaths(TransferKind kind, string physicalTarget, IEnumerable<TransferItem> items)
    {
        List<string> changed = [];
        foreach (TransferItem item in items)
        {
            if (kind != TransferKind.Copy)
                changed.Add(item.Source);
            if (kind != TransferKind.Delete)
                changed.Add(Path.Combine(physicalTarget, item.Name));
            if (!item.IsDirectory && kind != TransferKind.Copy)
                changed.Add(configs.ConfigPathOf(Path.GetDirectoryName(item.Source)!));
            if (!item.IsDirectory && kind != TransferKind.Delete)
                changed.Add(configs.ConfigPathOf(physicalTarget));
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
            // (A directory's is keyed as PasswordLocks keys it: an archived folder's by the folder, also from its unzipped copy)
            string protectedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (hash is not null)
                passwords.Add(new CarriedPassword(new Protection(isDirectory ? locations.ConfigDirectoryOf(protectedPath) : protectedPath, hash),
                    destination, isDirectory));
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
