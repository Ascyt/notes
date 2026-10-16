using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Pages.Services;

namespace notes.Pages;

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
        
        PhysicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        
        if (!System.IO.Path.Exists(PhysicalPath))
            return NotFound("Path not found.");

        if (System.IO.Directory.Exists(PhysicalPath))
        {
            Directory = new View.Directory(config, path)
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath
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

        PhysicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);

        string? create = Request.Query["create"];
        if (create is not null)
        {
            if (!System.IO.Directory.Exists(PhysicalPath))
                return NotFound("Directory not found.");
            if (create is not ("file" or "directory"))
                return BadRequest("Invalid create kind.");

            return Redirect("/" + directoryService.CreateEntry(PhysicalPath, create == "file"));
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

        PhysicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);

        if (System.IO.Directory.Exists(PhysicalPath))
            directoryService.Delete(PhysicalPath);
        else if (System.IO.File.Exists(PhysicalPath))
            fileService.Delete(PhysicalPath);
        else
            return NotFound("Path not found.");

        return new OkResult();
    }
}
