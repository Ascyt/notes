using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Helpers;
using notes.Features.Permissions;

namespace notes.Features.Components.Create;

/// <summary>Answers <c>?create=file</c> and <c>?create=directory</c>: makes a timestamp-named entry in the directory and redirects to it (a file to its editor).</summary>
public sealed class CreateHandler(Config config, IPathService path, AccessTree tree, StructureLock structureLock, FolderConfigStore configs, TimeProvider time)
{
    public async Task<IActionResult> HandleAsync(ClaimsPrincipal user, string physicalDirectory, string create)
    {
        if (!Directory.Exists(physicalDirectory))
            return new NotFoundObjectResult("Directory not found.");
        if (create is not ("file" or "directory"))
            return new BadRequestObjectResult("Invalid create kind.");
        // Creating in a directory the user may not read would tell them which names are taken in it
        if (!tree.GetAccess(user, physicalDirectory).CanRead)
            return new ObjectResult("No read access to this directory.") { StatusCode = StatusCodes.Status403Forbidden };

        bool isFile = create == "file";
        string baseName = time.GetLocalNow().ToString("yyyy-MM-dd_HH-mm");
        string extension = isFile ? ".md" : "";

        using (await structureLock.EnterAsync())
        {
            // A name that is taken is skipped (the user can see all names in the directory)
            string name = baseName + extension;
            for (int n = 1; Path.Exists(Path.Combine(physicalDirectory, name)); n++)
                name = $"{baseName}_{n}{extension}";

            string newPhysicalPath = Path.Combine(physicalDirectory, name);
            if (!tree.GetAccess(user, newPhysicalPath).CanCreate)
                return new ObjectResult($"Insufficient permissions to create a {create} here.") { StatusCode = StatusCodes.Status403Forbidden };

            if (isFile)
            {
                await File.WriteAllTextAsync(newPhysicalPath, "");
                configs.FileAdded(physicalDirectory, name);
            }
            else
                _ = Directory.CreateDirectory(newPhysicalPath);

            // A new file is there to be written: it opens in the editor, not as the (empty) preview of a markdown file
            return new RedirectResult("/" + path.ToVirtualPath(config.Dir, newPhysicalPath) + (isFile ? "?edit" : ""));
        }
    }
}
