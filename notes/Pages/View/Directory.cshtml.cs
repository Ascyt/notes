using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Pages.Services;

namespace notes.Pages.View;

public class Directory(Config config, IPathService path) : PageModel
{
    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";
    public string[] Directories { get; private set; } = []; 
    public string[] Files { get; private set; } = [];

    public void InitAsync()
    {
        Directories = System.IO.Directory.GetDirectories(PhysicalPath, "*", SearchOption.TopDirectoryOnly);
        Files = System.IO.Directory.GetFiles(PhysicalPath, "*", SearchOption.TopDirectoryOnly);
    }

    public string ToVirtualPath(string s)
        => path.ToVirtualPath(config.Dir, s);
}