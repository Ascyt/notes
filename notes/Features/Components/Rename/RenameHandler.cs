using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Bridges;
using notes.Features.Components.Transfer;
using notes.Features.Configuration;
using notes.Features.EntryActions;
using notes.Features.Helpers;
using notes.Features.History;

namespace notes.Features.Components.Rename;

/// <summary>
/// Answers <c>?rename=&lt;name&gt;</c>: renames the file or directory and returns where it is now (as an address). Renaming is moving within
/// the same directory, so it needs the same permissions as a move (see <see cref="TransferPlanner.CheckRename"/>).
/// </summary>
public sealed class RenameHandler(NotesLocations locations, IPathService path, TransferPlanner planner, StructureLock structureLock, FolderConfigStore configs, HistoryService history) : IEntryPostAction
{
    public const string RenameParameter = "rename";

    public string Parameter => RenameParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        (ClaimsPrincipal user, string virtualPath, string physicalPath, string requestedName) = (request.User, request.VirtualPath, request.PhysicalPath, request.Value);
        if (virtualPath == "")
            return new BadRequestObjectResult("Cannot rename the root directory.");
        if (NewNames.Check(requestedName, out string newName) is { } invalid)
            return new BadRequestObjectResult(invalid);

        using (await structureLock.EnterAsync())
        {
            bool isDirectory = Directory.Exists(physicalPath);
            if (!isDirectory && !File.Exists(physicalPath))
                return new NotFoundObjectResult("Path not found.");

            // (A bridge is never renamed, the planner refuses it, but where it is mounted is what the directory above has)
            string directory = locations.ParentOf(physicalPath)!;
            string newPhysicalPath = Path.Combine(directory, newName);
            // Differing only in case is the same entry, so that is allowed. (The user can see all names in the directory: they can see this entry.)
            if (!locations.IsFree(newPhysicalPath) && !string.Equals(newPhysicalPath, physicalPath, StringComparison.OrdinalIgnoreCase))
                return new ConflictObjectResult("A file or directory with that name already exists.");

            try
            {
                // (Also checks the length of the new path of everything in the tree)
                planner.CheckRename(user, physicalPath, newPhysicalPath);
            }
            catch (TransferException e)
            {
                return new ObjectResult(e.Message) { StatusCode = e.StatusCode };
            }

            string[] changed = isDirectory
                ? [physicalPath, newPhysicalPath]
                : [physicalPath, newPhysicalPath, Path.Combine(directory, PathService.ConfigFileName)];
            history.BeforeChange(user, changed);
            if (isDirectory)
                // (Its own configuration file is in it, and goes along in the same step)
                Directory.Move(physicalPath, newPhysicalPath);
            else
                RenameFile(directory, physicalPath, newPhysicalPath);
            history.AfterChange(user, changed, HistoryChangeKind.Change);

            return new ContentResult { Content = VirtualPaths.ToAddress(path.ToVirtualPath(newPhysicalPath)) };
        }
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
