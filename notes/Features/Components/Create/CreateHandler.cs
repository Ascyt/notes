using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.Configuration;
using notes.Features.EntryActions;
using notes.Features.Helpers;
using notes.Features.History;

namespace notes.Features.Components.Create;

/// <summary>Answers <c>?create=file</c> and <c>?create=directory</c>: makes a timestamp-named entry in the directory and redirects to it (a file to its editor).</summary>
public sealed class CreateHandler(NotesLocations locations, IPathService path, AccessTree tree, StructureLock structureLock, FolderConfigStore configs, HistoryService history, LimitsConfig limits, TimeProvider time) : IEntryPostAction
{
    public const string CreateParameter = "create";

    public string Parameter => CreateParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        (ClaimsPrincipal user, string physicalDirectory, string create) = (request.User, request.PhysicalPath, request.Value);
        if (create is not ("file" or "directory"))
            return new BadRequestObjectResult("Invalid create kind.");

        bool isFile = create == "file";
        string baseName = time.GetLocalNow().ToString("yyyy-MM-dd_HH-mm");
        string extension = isFile ? ".md" : "";

        using (await structureLock.EnterAsync())
        {
            // Checked under the lock: the directory could otherwise be moved, or another made in its place, after it was checked
            if (!Directory.Exists(physicalDirectory))
                return new NotFoundObjectResult("Directory not found.");
            // Creating in a directory the user may not read would tell them which names are taken in it
            if (!tree.GetAccess(user, physicalDirectory).CanRead)
                return new ObjectResult("No read access to this directory.") { StatusCode = StatusCodes.Status403Forbidden };

            // A name that is taken is skipped (the user can see all names in the directory)
            string name = baseName + extension;
            for (int n = 1; !locations.IsFree(Path.Combine(physicalDirectory, name)); n++)
                name = $"{baseName}_{n}{extension}";

            string newPhysicalPath = Path.Combine(physicalDirectory, name);
            if (!tree.GetAccess(user, newPhysicalPath).CanCreate)
                return new ObjectResult($"Insufficient permissions to create a {create} here.") { StatusCode = StatusCodes.Status403Forbidden };
            if (limits.IsTooLong(path.ToVirtualPath(newPhysicalPath)))
                return new BadRequestObjectResult(limits.PathTooLongMessage(path.ToVirtualPath(newPhysicalPath)));

            // The configuration of the directory gets an entry for a new file
            string[] changed = isFile ? [newPhysicalPath, Path.Combine(physicalDirectory, PathService.ConfigFileName)] : [newPhysicalPath];
            history.BeforeChange(user, changed);
            if (isFile)
            {
                await File.WriteAllTextAsync(newPhysicalPath, "");
                configs.FileAdded(physicalDirectory, name);
            }
            else
                _ = Directory.CreateDirectory(newPhysicalPath);
            history.AfterChange(user, changed, HistoryChangeKind.Change);

            // A new file is there to be written: it opens in the editor, not as the (empty) preview of a markdown file
            return new RedirectResult(VirtualPaths.ToAddress(path.ToVirtualPath(newPhysicalPath)) + (isFile ? "?edit" : ""));
        }
    }
}
