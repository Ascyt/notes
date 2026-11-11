using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Breadcrumb;

/// <summary>
/// A directory above the entry that an earlier state is shown of, which can't be seen as it was itself: <paramref name="Reason"/> says why.
/// It leads to the directory as it is now, whose page says so too, unless it isn't there now (<paramref name="ExistsNow"/>).
/// </summary>
public sealed record ParentWithoutHistory(string Reason, bool ExistsNow);

public class _Breadcrumb : PageModel
{
    /// <summary>Whether the current entry is a file (otherwise a directory), used for the icon.</summary>
    public bool IsFile { get; init; }

    public string VirtualPath { get; init; } = "";

    /// <summary>Why the current entry can't be renamed, or null if it can (see <see cref="Rename._RenameInput.DisabledReason"/>).</summary>
    public string? RenameDisabledReason { get; init; }

    /// <summary>
    /// In an earlier state: the directories above that can't be seen as they were, by virtual path ("" for the root, in any case), shown with a
    /// crossed-out calendar like such entries of a listing.
    /// </summary>
    public IReadOnlyDictionary<string, ParentWithoutHistory> ParentsWithoutHistory { get; init; } = new Dictionary<string, ParentWithoutHistory>();

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
