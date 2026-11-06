using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Breadcrumb;

public class _Breadcrumb : PageModel
{
    /// <summary>Whether the current entry is a file (otherwise a directory), used for the icon.</summary>
    public bool IsFile { get; init; }

    public string VirtualPath { get; init; } = "";

    /// <summary>Why the current entry can't be renamed, or null if it can (see <see cref="Rename._RenameInput.DisabledReason"/>).</summary>
    public string? RenameDisabledReason { get; init; }

    private string[] Names => VirtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public bool IsRoot => Names.Length == 0;

    /// <summary>Name of the current entry, shown as the rename input.</summary>
    public string Name => IsRoot ? "" : Names[^1];

    /// <summary>Virtual path of the parent directory ("" for the root), or null when the current entry is the root.</summary>
    public string? ParentVirtualPath => IsRoot ? null : string.Join('/', Names[..^1]);

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
