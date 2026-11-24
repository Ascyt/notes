using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Bridges;
using Notes.Features.Components.Transfer;
using Notes.Features.Configuration;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.Components.Rename;

/// <summary>
/// Answers <c>?rename=&lt;name&gt;</c>: renames the file or directory and returns where it is now (as an address). Renaming is moving within
/// the same directory, so it needs the same permissions as a move (see <see cref="TransferPlanner.CheckRename"/>).
/// </summary>
public sealed class RenameHandler(EntryLocations locations, IPathService path, TransferPlanner planner, StructureLock structureLock,
    FolderConfigStore configs, HistoryService history, EntryTexts entryTexts, NameTexts nameTexts, Passwords.PasswordUnlocks unlocks,
    ZippedFolders.ArchivedFolders archives, TransferTexts transferTexts, Auth.AccessTree tree, Passwords.PasswordLocks passwordLocks,
    ILogger<RenameHandler> logger) : IEntryPostAction
{
    public const string RenameParameter = "rename";

    public string Parameter => RenameParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        (ClaimsPrincipal user, string virtualPath, string physicalPath, string requestedName) = (request.User, request.VirtualPath,
            request.PhysicalPath, request.Value);
        if (virtualPath == "")
            return new BadRequestObjectResult(entryTexts.RootCantBeRenamed);
        if (NewNames.Check(requestedName, out string newName) is { } invalid)
            return new BadRequestObjectResult(nameTexts.Problem(invalid));

        using (await structureLock.EnterAsync())
        {
            bool isDirectory = Directory.Exists(physicalPath);
            if (!isDirectory && !File.Exists(physicalPath))
                return new NotFoundObjectResult(entryTexts.PathNotFound);

            // (A bridge is never renamed, the planner refuses it, but where it is mounted is what the directory above has)
            string directory = locations.ParentOf(physicalPath)!;
            string newPhysicalPath = Path.Combine(directory, newName);
            // Differing only in case is the same entry, so that is allowed. (The user can see all names in the directory: they can see this entry.)
            if (!locations.IsFree(newPhysicalPath) && !string.Equals(newPhysicalPath, physicalPath, StringComparison.OrdinalIgnoreCase))
                return new ConflictObjectResult(entryTexts.NameTaken);

            // What is in the archived folders in it (or that it is) is checked as for any directory: unzipped for that (those the user may
            // open; the gate unzipped the entry itself), and kept so while it is checked
            if (isDirectory)
            {
                try
                {
                    await archives.UnzipAllAtOrBelowAsync([physicalPath],
                        folder => tree.CanOpen(user, folder) && passwordLocks.FirstLocked(folder) is null);
                }
                catch (IOException e)
                {
                    logger.LogError(e, "An archived folder in {path} could not be unzipped for a rename", physicalPath);
                    return new ObjectResult(transferTexts.ArchivedFolderUnzipFailed) { StatusCode = StatusCodes.Status500InternalServerError };
                }
            }

            using (await archives.UseAllAroundAsync([physicalPath]))
            {
                try
                {
                    // (Also checks the length of the new path of everything in the tree)
                    planner.CheckRename(user, physicalPath, newPhysicalPath);
                }
                catch (TransferException e)
                {
                    return new ObjectResult(e.Message) { StatusCode = e.StatusCode };
                }
            }

            if (!isDirectory)
                return Rename(request.Context, user, directory, physicalPath, newPhysicalPath, isDirectory: false);

            // What is archived at or in it is mounted where it is now: zipped first (and kept so meanwhile), it moves with it as its archive,
            // and is unzipped again where it is then. The entry is then the folder itself, next to its archive, rather than its unzipped copy.
            try
            {
                return await archives.WhileZippedAsync([physicalPath],
                    folders => Task.FromResult(Rename(request.Context, user, directory, folders[0], newPhysicalPath, isDirectory: true)));
            }
            catch (IOException e)
            {
                logger.LogWarning(e, "What is archived in {path} could not be zipped for a rename", physicalPath);
                return new ConflictObjectResult(entryTexts.InUse);
            }
        }
    }

    private IActionResult Rename(HttpContext context, ClaimsPrincipal user, string directory, string physicalPath, string newPhysicalPath,
        bool isDirectory)
    {
        string[] changed = isDirectory
            ? [physicalPath, newPhysicalPath]
            : [physicalPath, newPhysicalPath, configs.ConfigPathOf(directory)];
        history.BeforeChange(user, changed);
        if (isDirectory)
            // (Its own configuration file is in it, and goes along in the same step)
            Directory.Move(physicalPath, newPhysicalPath);
        else
            RenameFile(directory, physicalPath, newPhysicalPath);
        history.AfterChange(user, changed, HistoryChangeKind.Change);
        // Its passwords went along (in its configuration, or its entry), and so does what was unlocked of them
        unlocks.TreeMoved(context, physicalPath, newPhysicalPath);

        return new ContentResult { Content = VirtualPaths.ToAddress(path.ToVirtualPath(newPhysicalPath)) };
    }

    /// <summary>Renames the file with its entry in the configuration of the folder, so that it has its own permissions all the time.</summary>
    private void RenameFile(string directory, string physicalPath, string newPhysicalPath)
    {
        string oldName = Path.GetFileName(physicalPath);
        string newName = Path.GetFileName(newPhysicalPath);
        configs.FileRenaming(directory, oldName, newName);
        try
        {
            File.Move(physicalPath, newPhysicalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The entry made for the new name must not stay behind for a file that isn't there: a later one of that name would get it
            if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                configs.FileRemoved(directory, newName);
            throw;
        }

        configs.FileRenamed(directory, oldName, newName);
    }
}
