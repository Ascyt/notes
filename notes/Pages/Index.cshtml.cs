using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Pages.Partials;
using notes.Pages.Services;

namespace notes.Pages;

public sealed class IndexModel(Config config, IPathService path) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public NDirectory? NDirectory { get; private set; } = null;

    public IActionResult OnGet()
    {
        VirtualPath = Request.Path.Value!;
        if (VirtualPath.StartsWith("/_"))
            return NotFound("Page not found.");
        
        PhysicalPath = path.GetPhysicalPath(config.Dir, VirtualPath.TrimStart('/'));
        
        if (!Path.Exists(PhysicalPath))
            return NotFound("Path not found.");

        if (Directory.Exists(PhysicalPath))
        {
            NDirectory = new NDirectory
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath
            };
        }

        return Page();
    }
}
