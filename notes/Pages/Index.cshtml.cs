using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Pages.Partials;
using notes.Pages.Services;
using Directory = notes.Pages.Partials.Directory;

namespace notes.Pages;

public sealed class IndexModel(Config config, IPathService path) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public Directory? Directory { get; private set; }

    public IActionResult OnGet()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        if (VirtualPath.StartsWith("_/"))
            return NotFound("Page not found.");
        
        PhysicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        
        if (!System.IO.Path.Exists(PhysicalPath))
            return NotFound("Path not found.");

        if (System.IO.Directory.Exists(PhysicalPath))
        {
            Directory = new Directory(config, path)
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath
            };
            Directory.InitAsync();
        }
        else if (System.IO.File.Exists(PhysicalPath))
        {
            
        }

        return Page();
    }
}
