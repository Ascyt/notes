using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Auth;
using notes.Features.Components.Selection;
using notes.Features.Helpers;

namespace notes.Features.View.Browse;

/// <summary>An entry of the listing.</summary>
/// <param name="Path">Its physical path.</param>
/// <param name="CanOpen">Whether the user may read it. If not, it is only listed by its name: it can't be opened or selected.</param>
public sealed record ListedEntry(string Path, bool CanOpen);

public class DirectoryView(Config config, IPathService path, AccessTree tree) : PageModel
{
    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";

    /// <summary>The user looking at the directory.</summary>
    public ClaimsPrincipal Viewer { get; init; } = new();

    /// <summary>
    /// The subdirectories: for a logged-in user all of them (who may read a directory sees the names of everything in it), for an anonymous
    /// user only those they may open.
    /// </summary>
    public ListedEntry[] Directories { get; private set; } = [];

    /// <summary>The files, chosen like <see cref="Directories"/>.</summary>
    public ListedEntry[] Files { get; private set; } = [];

    /// <summary>Whether the user may see what is in the directory; without it the listing stays empty.</summary>
    public bool CanBrowse { get; init; }

    /// <summary>Whether the page offers selecting entries. It needs the user's own id (the selection is kept under it), so not for someone who is not logged in.</summary>
    public bool CanSelect => Viewer.Identity?.IsAuthenticated == true;

    /// <summary>Whether the user may manage permissions, so the page has the way to the permissions of the directory.</summary>
    public bool CanConfigure { get; init; }

    /// <summary>Why the user can't delete the directory, or null if they can.</summary>
    public string? DeleteDeniedReason { get; private set; }

    /// <summary>Why the user can't rename the directory, or null if they can.</summary>
    public string? RenameDeniedReason { get; private set; }

    /// <summary>Why the user can't add entries (create, upload, copy or move here), or null if they can.</summary>
    public string? CreateDeniedReason { get; private set; }

    /// <summary>
    /// Whether the user can't change anything here: neither add entries nor delete the directory. The page then doesn't offer the buttons
    /// that change things (create, upload, delete) at all, instead of showing them disabled.
    /// </summary>
    public bool IsReadOnly => CreateDeniedReason is not null && DeleteDeniedReason is not null;

    /// <summary>What the user has selected (anywhere in the notes) when the page is made.</summary>
    public SelectionSnapshot Selection { get; init; } = SelectionSnapshot.Empty;

    public bool IsRoot => VirtualPath == "";

    /// <summary>The path of the directory as the listing names it, which (other than <see cref="VirtualPath"/>) is the same however the address was typed.</summary>
    public string CanonicalVirtualPath
        => ToVirtualPath(PhysicalPath) is var virtualPath and not "." ? virtualPath : "";

    public bool IsSelected(string virtualPath)
        => Selection.Paths.Contains(virtualPath, StringComparer.OrdinalIgnoreCase);

    public void Init()
    {
        if (!CanBrowse)
            return;

        ListedEntry ToListed(string entry) => new(entry, tree.GetAccess(Viewer, entry).CanRead);
        // Anonymous users don't see the names of what they may not read at all (see AccessTree.SeesUnreadableNames)
        bool seesUnreadable = tree.SeesUnreadableNames(Viewer);

        Directories = [..Directory.GetDirectories(PhysicalPath, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => Path.GetFileName(directory) != PathService.ReservedName)
            .OrderByDescending(directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
            .Select(ToListed)
            .Where(entry => entry.CanOpen || seesUnreadable)];
        // The file with the permissions is not a note: it is changed through the permissions page, and never listed
        Files = [..Directory.GetFiles(PhysicalPath, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(Path.GetFileName(file), PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .Select(ToListed)
            .Where(entry => entry.CanOpen || seesUnreadable)];

        // The root can't be deleted or renamed anyway, so there is no need to look at all of the notes
        if (IsRoot)
            DeleteDeniedReason = "The root directory can't be deleted.";
        else
        {
            TreeAccess inspected = tree.Inspect(Viewer, PhysicalPath);
            DeleteDeniedReason = inspected.DeleteDeniedReason;
            RenameDeniedReason = tree.RenameDeniedReason(Viewer, PhysicalPath, inspected);
        }

        CreateDeniedReason = tree.CreateDeniedReason(Viewer, PhysicalPath);
    }

    /// <summary>Shortcut for the entry at <paramref name="index"/>: the modifiers plus 1-9, then 0. Null after the first ten.</summary>
    public static string? NumberKey(int index, string modifiers)
        => index < 10 ? modifiers + (index + 1) % 10 : null;

    public string ToVirtualPath(string s)
        => path.ToVirtualPath(config.Dir, s);
}
