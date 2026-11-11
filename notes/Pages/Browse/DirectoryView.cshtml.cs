using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Auth;
using notes.Features.Components.Breadcrumb;
using notes.Features.Components.Selection;
using notes.Features.Configuration;
using notes.Features.Helpers;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Requests;
using notes.Features.HistoryViewing.States;
using notes.Features.HistoryViewing.Views;

namespace notes.Pages.Browse;

/// <summary>How an entry of the listing is shown.</summary>
public enum ListedKind
{
    /// <summary>A link to it, and it can be selected.</summary>
    Open,

    /// <summary>Only its name, with a lock: the user may not read it.</summary>
    Locked,

    /// <summary>In an earlier state: only its name, with a crossed-out calendar, as its history can't be seen.</summary>
    NoHistory,
}

/// <summary>An entry of the listing.</summary>
/// <param name="Path">Its physical path.</param>
/// <param name="Kind">How it is shown. Only an open entry can be opened or selected.</param>
/// <param name="ExistsNow">In an earlier state: whether there is an entry at its path now, which an entry without history still links to.</param>
public sealed record ListedEntry(string Path, ListedKind Kind, bool ExistsNow = true)
{
    public bool CanOpen => Kind == ListedKind.Open;
}

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

    /// <summary>Which earlier state is shown (<c>?before=</c> or <c>?commit=</c>), or null for the directory as it is.</summary>
    public EarlierStateParameter? Shown { get; init; }

    public bool ShowsEarlierState => Shown is not null;

    /// <summary>
    /// Whether the page offers selecting entries. It needs the user's own id (the selection is kept under it), so not for someone who is not
    /// logged in, and it is for changing things, so not in an earlier state.
    /// </summary>
    public bool CanSelect => Viewer.Identity?.IsAuthenticated == true && !ShowsEarlierState;

    /// <summary>Whether the user may manage permissions, so the page has the way to the permissions of the directory.</summary>
    public bool CanConfigure { get; init; }

    /// <summary>Whether the page has the way to the earlier states of the directory.</summary>
    public HistoryButtonState HistoryButton { get; init; } = HistoryButtonState.Hidden;

    /// <summary>In the view of a commit, the sidebar of the list of changes with what it shows from the start; otherwise null (it starts empty).</summary>
    public _CommitSidebar? CommitSidebar { get; init; }

    /// <summary>In an earlier state: the directories above that can't be seen as they were, for the breadcrumb.</summary>
    public IReadOnlyDictionary<string, ParentWithoutHistory> ParentsWithoutHistory { get; init; } = new Dictionary<string, ParentWithoutHistory>();

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
    public bool IsReadOnly => ShowsEarlierState || (CreateDeniedReason is not null && DeleteDeniedReason is not null);

    /// <summary>What the user has selected (anywhere in the notes) when the page is made.</summary>
    public SelectionSnapshot Selection { get; init; } = SelectionSnapshot.Empty;

    public bool IsRoot => VirtualPath == "";

    /// <summary>The path of the directory as the listing names it, which (other than <see cref="VirtualPath"/>) is the same however the address was typed.</summary>
    public string CanonicalVirtualPath
        => ToVirtualPath(PhysicalPath) is var virtualPath and not "." ? virtualPath : "";

    private HashSet<string>? _selectedPaths;

    /// <summary>Asked for every entry of the listing, so not a search through the selection each time.</summary>
    public bool IsSelected(string virtualPath)
        => (_selectedPaths ??= new HashSet<string>(Selection.Paths, StringComparer.OrdinalIgnoreCase)).Contains(virtualPath);

    public void Init()
    {
        if (!CanBrowse)
            return;

        // Every entry (and everything inside this directory, for what the page says about deleting it) asks about the same folders:
        // read their configuration once, not once per entry
        using IDisposable scope = ConfigReadScope.Begin();

        // The listing knows which are directories, so the permissions needn't look at the disk for each entry
        Func<string, ListedEntry> ToListed(bool isDirectory) => entry => new(entry, tree.GetAccess(Viewer, entry, isDirectory).CanRead ? ListedKind.Open : ListedKind.Locked);
        // Anonymous users don't see the names of what they may not read at all (see AccessTree.SeesUnreadableNames)
        bool seesUnreadable = tree.SeesUnreadableNames(Viewer);

        Directories = [..Directory.GetDirectories(PhysicalPath, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => Path.GetFileName(directory) != PathService.ReservedName)
            .OrderByDescending(directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
            .Select(ToListed(isDirectory: true))
            .Where(entry => entry.CanOpen || seesUnreadable)];
        // The configuration file is not a note: it is changed through the configuration page, and never listed
        Files = [..Directory.GetFiles(PhysicalPath, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(Path.GetFileName(file), PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .Select(ToListed(isDirectory: false))
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

    /// <summary>
    /// Fills the listing with the entries of an earlier state (<see cref="Shown"/>), as <see cref="EarlierStatePermissions"/> says they are
    /// shown. Nothing in it can be changed.
    /// </summary>
    public void InitFromEarlierState(IEnumerable<(ListedEntry Entry, bool IsDirectory)> entries)
    {
        (ListedEntry Entry, bool IsDirectory)[] all = [.. entries.OrderByDescending(listed => Path.GetFileName(listed.Entry.Path), StringComparer.OrdinalIgnoreCase)];
        Directories = [.. all.Where(listed => listed.IsDirectory).Select(listed => listed.Entry)];
        Files = [.. all.Where(listed => !listed.IsDirectory).Select(listed => listed.Entry)];
        DeleteDeniedReason = RenameDeniedReason = CreateDeniedReason = EarlierStateViews.CantBeChangedReason;
    }

    /// <summary>The row of an entry of the listing that can't be opened (see <see cref="ListedEntry.CanOpen"/>).</summary>
    public _UnopenableEntry UnopenableRow(ListedEntry entry, int index, bool isDirectory)
    {
        // An entry without history leads to the entry as it is now, whose page says why; one that is gone now leads nowhere, nor does a locked one
        string? href = entry.Kind == ListedKind.NoHistory && entry.ExistsNow ? "/" + ToVirtualPath(entry.Path) : null;
        return new _UnopenableEntry
        {
            Name = Path.GetFileName(entry.Path),
            IsDirectory = isDirectory,
            Kind = entry.Kind,
            Number = index + 1,
            Href = href,
            Key = href is null ? null : NumberKey(index, isDirectory ? "" : "+"),
            ShowsCheckbox = CanSelect
        };
    }

    /// <summary>Shortcut for the entry at <paramref name="index"/>: the modifiers plus 1-9, then 0. Null after the first ten.</summary>
    public static string? NumberKey(int index, string modifiers)
        => index < 10 ? modifiers + (index + 1) % 10 : null;

    public string ToVirtualPath(string s)
        => path.ToVirtualPath(config.Dir, s);
}
