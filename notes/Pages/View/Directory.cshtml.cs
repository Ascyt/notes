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

    /// <summary>Shortcut for the entry at <paramref name="index"/>: the modifiers plus 1-9, then 0. Null after the first ten.</summary>
    public static string? NumberKey(int index, string modifiers)
        => index < 10 ? modifiers + (index + 1) % 10 : null;

    public string ToVirtualPath(string s)
        => path.ToVirtualPath(config.Dir, s);
}