using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.View.Editor;

public class FileView : PageModel
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
