using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.View;

public class File : PageModel
{
    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";
    public string Contents { get; private set; } = "";
    
    public string Language => Path.GetExtension(VirtualPath).ToLowerInvariant() switch
    {
        ".md" => "markdown",
        ".json" => "json",
        _ => "plaintext"
    };

    public async Task InitAsync()
    {
        Contents = await System.IO.File.ReadAllTextAsync(PhysicalPath);
    }
}