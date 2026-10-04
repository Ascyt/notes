using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.View;

public class File : PageModel
{
    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";
    public string Contents { get; private set; } = "";
    
    public async Task InitAsync()
    {
        Contents = await System.IO.File.ReadAllTextAsync(PhysicalPath);
    }
}