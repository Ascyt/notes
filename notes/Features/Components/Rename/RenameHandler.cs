using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Components.Transfer;
using notes.Features.Helpers;

namespace notes.Features.Components.Rename;

/// <summary>
/// Answers <c>?rename=&lt;name&gt;</c>: renames the file or directory and returns where it is now. Renaming is moving within the same
/// directory, so it needs the same permissions as a move (see <see cref="TransferPlanner.CheckRename"/>).
/// </summary>
public sealed class RenameHandler(Config config, IPathService path, TransferPlanner planner, StructureLock structureLock)
{
    public async Task<IActionResult> HandleAsync(ClaimsPrincipal user, string virtualPath, string physicalPath, string newName)
    {
        if (virtualPath == "")
            return new BadRequestObjectResult("Cannot rename the root directory.");
        if (!path.IsValidName(newName))
            return new BadRequestObjectResult("Invalid name.");

        using (await structureLock.EnterAsync())
        {
            bool isDirectory = Directory.Exists(physicalPath);
            if (!isDirectory && !File.Exists(physicalPath))
                return new NotFoundObjectResult("Path not found.");

            string newPhysicalPath = Path.Combine(Path.GetDirectoryName(physicalPath)!, newName);
            // Differing only in case is the same entry, so that is allowed. (The user can see all names in the directory: they can see this entry.)
            if (Path.Exists(newPhysicalPath) && !string.Equals(newPhysicalPath, physicalPath, StringComparison.OrdinalIgnoreCase))
                return new ConflictObjectResult("A file or directory with that name already exists.");

            try
            {
                planner.CheckRename(user, physicalPath, newPhysicalPath);
            }
            catch (TransferException e)
            {
                return new ObjectResult(e.Message) { StatusCode = e.StatusCode };
            }

            if (isDirectory)
                Directory.Move(physicalPath, newPhysicalPath);
            else
                File.Move(physicalPath, newPhysicalPath);

            return new ContentResult { Content = "/" + path.ToVirtualPath(config.Dir, newPhysicalPath) };
        }
    }
}
