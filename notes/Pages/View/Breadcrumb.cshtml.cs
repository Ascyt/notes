using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.View;

public class Breadcrumb : PageModel
{
    /// <summary>Whether the current entry is a file (otherwise a directory), used for the icon.</summary>
    public bool IsFile { get; init; }

    public string VirtualPath { get; init; } = "";

    private string[] Names => VirtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public bool IsRoot => Names.Length == 0;

    /// <summary>Name of the current entry, shown as the rename input.</summary>
    public string Name => IsRoot ? "" : Names[^1];

    /// <summary>Each parent of the current entry with the virtual path it links to.</summary>
    public IEnumerable<(string Name, string Path)> Parents
    {
        get
        {
            string[] names = Names;
            for (int i = 0; i < names.Length - 1; i++)
                yield return (names[i], string.Join('/', names[..(i + 1)]));
        }
    }
}
