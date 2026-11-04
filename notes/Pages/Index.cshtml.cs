using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Auth;
using notes.Pages.Services;

namespace notes.Pages;

[Authorize(Policy = NotesAccess.PolicyName)]
public sealed class IndexModel(Config config, 
    IPathService path, 
    IFileService fileService, 
    IDirectoryService directoryService) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public View.Directory? Directory { get; private set; }
    public new View.File? File { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        if (VirtualPath.StartsWith("_/"))
            return NotFound("Page not found.");
        
        string? physicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        if (physicalPath is null || !System.IO.Path.Exists(physicalPath))
            return NotFound("Path not found.");

        PhysicalPath = physicalPath;

        if (System.IO.Directory.Exists(PhysicalPath))
        {
            Directory = new View.Directory(config, path)
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath,
                CanBrowse = User.IsInRole(NotesAccess.AdminRole)
            };
            Directory.InitAsync();
        }
        else if (System.IO.File.Exists(PhysicalPath))
        {
            File = new View.File()
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath
            };
            await File.InitAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        if (VirtualPath.StartsWith("_/"))
            return NotFound("Page not found.");

        string? physicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        if (physicalPath is null)
            return NotFound("Path not found.");

        PhysicalPath = physicalPath;

        string? create = Request.Query["create"];
        if (create is not null)
        {
            if (!System.IO.Directory.Exists(PhysicalPath))
                return NotFound("Directory not found.");
            if (create is not ("file" or "directory"))
                return BadRequest("Invalid create kind.");

            return Redirect("/" + directoryService.CreateEntry(PhysicalPath, create == "file"));
        }

        // Uploads: ?upload=start returns an id made by the server, then the file follows as a sequence of raw chunks
        // (?upload=<id>&name=..&offset=..&last=..) so that neither memory nor the request size limits of the server
        // or proxies cap the file size. ?upload=<id>&abort discards an upload.
        string? upload = Request.Query["upload"];
        if (upload is not null)
        {
            if (!System.IO.Directory.Exists(PhysicalPath))
                return NotFound("Directory not found.");

            // The user's own folder holds their uploads, so the id means nothing to anyone else
            string? userId = User.FindFirst("sub")?.Value;
            if (userId is null)
                return Forbid();

            if (upload == "start")
                return Content(fileService.StartUpload(userId));

            if (!Guid.TryParse(upload, out Guid uploadId))
                return BadRequest("Invalid upload id.");

            if (Request.Query["abort"].Count > 0)
            {
                fileService.AbortUpload(userId, uploadId);
                return new OkResult();
            }

            string name = Request.Query["name"].ToString();
            if (!path.IsValidName(name))
                return BadRequest("Invalid file name.");
            if (!long.TryParse(Request.Query["offset"], out long offset) || offset < 0)
                return BadRequest("Invalid offset.");

            string? result = await fileService.UploadChunkAsync(userId, uploadId, PhysicalPath, name, offset, Request.Query["last"].Count > 0, Request.Body);
            return result is null
                ? new ConflictObjectResult("No such upload, or the chunk does not continue it.")
                : Content(result);
        }

        string? rename = Request.Query["rename"];
        if (rename is not null)
        {
            if (VirtualPath == "")
                return BadRequest("Cannot rename the root directory.");
            if (!path.IsValidName(rename))
                return BadRequest("Invalid name.");

            string? newVirtualPath;
            if (System.IO.Directory.Exists(PhysicalPath))
                newVirtualPath = directoryService.Rename(PhysicalPath, rename);
            else if (System.IO.File.Exists(PhysicalPath))
                newVirtualPath = fileService.Rename(PhysicalPath, rename);
            else
                return NotFound("Path not found.");

            return newVirtualPath is null
                ? new ConflictObjectResult("A file or directory with that name already exists.")
                : Content("/" + newVirtualPath);
        }

        if (!System.IO.File.Exists(PhysicalPath))
            return NotFound("File not found.");

        await fileService.SaveAsync(PhysicalPath, Request.Body);

        return new OkResult();
    }

    public IActionResult OnDelete()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        if (VirtualPath.StartsWith("_/"))
            return NotFound("Page not found.");
        if (VirtualPath == "")
            return BadRequest("Cannot delete the root directory.");

        string? physicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        if (physicalPath is null)
            return NotFound("Path not found.");

        PhysicalPath = physicalPath;

        if (System.IO.Directory.Exists(PhysicalPath))
            directoryService.Delete(PhysicalPath);
        else if (System.IO.File.Exists(PhysicalPath))
            fileService.Delete(PhysicalPath);
        else
            return NotFound("Path not found.");

        return new OkResult();
    }
}
