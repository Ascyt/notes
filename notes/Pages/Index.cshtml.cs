using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Pages.Services;

namespace notes.Pages;

public sealed class IndexModel(Config config, IPathService path) : PageModel
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
}
