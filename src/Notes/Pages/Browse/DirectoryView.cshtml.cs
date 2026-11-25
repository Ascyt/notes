using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Components.Assets;
using Notes.Features.Components.Breadcrumb;
using Notes.Features.Components.Selection;
using Notes.Features.Components.Toolbar;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.HistoryViewing.Changes;
using Notes.Features.HistoryViewing.Commits;
using Notes.Features.HistoryViewing.Requests;
using Notes.Features.HistoryViewing.States;
using Notes.Features.HistoryViewing.Views;
using Notes.Features.Markdown;
using Notes.Features.Media;

namespace Notes.Pages.Browse;

/// <summary>How an entry of the listing is shown.</summary>
public enum ListedKind
{
    /// <summary>A link to it, and it can be selected.</summary>
    Open,

    /// <summary>Only its name, with a lock: the user may not read it.</summary>
    Locked,

    /// <summary>
    /// Its name, with a lock of its own: a password locks it, which opening it asks for (it, or a directory above, that the user could
    /// otherwise read). It can't be selected.
    /// </summary>
    PasswordLocked,

    /// <summary>In an earlier state: only its name, with a crossed-out calendar, as its history can't be seen.</summary>
    NoHistory,
}

/// <summary>An entry of the listing.</summary>
/// <param name="Path">Its physical path.</param>
/// <param name="Kind">How it is shown. Only an open entry can be opened or selected.</param>
/// <param name="ExistsNow">In an earlier state: whether there is an entry at its path now, which an entry without history still links to.</param>
/// <param name="Mark">In the changes of a commit: what it did to the entry (null for a directory with changes below it, and elsewhere).</param>
public sealed record ListedEntry(string Path, ListedKind Kind, bool ExistsNow = true, ChangeMark? Mark = null)
{
    /// <summary>
    /// The name it is shown with in place of its own, or null for its own: the title of a markdown file (<see cref="MarkdownTitles"/>). Only
    /// for an entry that can be opened, as the title is part of what is in it.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Whether it has a password of its own that is unlocked, which an open lock after its name shows. Only for an entry that can be opened (as
    /// it is now): one that is still locked has its lock instead.
    /// </summary>
    public bool IsUnlocked { get; init; }

    /// <summary>
    /// How it is kept if it is an archived folder (zipped or unzipped now), which a zipper after its name shows; otherwise null. Only for an
    /// entry that can be opened.
    /// </summary>
    public Features.ZippedFolders.ArchiveState? Archived { get; init; }

    /// <summary>Whether that archive is encrypted, which a shield in place of the zipper shows.</summary>
    public bool ArchiveEncrypted { get; init; }

    /// <summary>How far its zipping got (0 to 1), while <see cref="Archived"/> is <c>Zipping</c>.</summary>
    public double ZipProgress { get; init; }

    public bool CanOpen => Kind == ListedKind.Open;
}

/// <summary>
/// The listing of a directory (<c>DirectoryView.cshtml</c>), with its parts: the groups of directories and files
/// (<see cref="EntryTable._EntryGroup"/>), the grid of the media (<see cref="MediaGrid._MediaGroup"/>).
/// </summary>
public class DirectoryView(EntryLocations locations, IPathService path, AccessTree tree) : PageModel
{
    /// <summary>The styles that all parts of the listing share, by their path in the library (see <see cref="ComponentAssets"/>).</summary>
    public const string Styles = "Pages/Browse/DirectoryView.css";

    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";

    /// <summary>The user looking at the directory.</summary>
    public ClaimsPrincipal Viewer { get; init; } = new();

    /// <summary>
    /// The subdirectories: for a logged-in user all of them (who may read a directory sees the names of everything in it), for an anonymous
    /// user only those they may open.
    /// </summary>
    public ListedEntry[] Directories { get; private set; } = [];

    /// <summary>The files, chosen like <see cref="Directories"/>, but without the media.</summary>
    public ListedEntry[] Files { get; private set; } = [];

    /// <summary>The images and videos (<see cref="MediaEntries"/>), chosen like the files, which are shown in a grid of their own.</summary>
    public ListedEntry[] Media { get; private set; } = [];

    /// <summary>Whether the user may see what is in the directory; without it the listing stays empty.</summary>
    public bool CanBrowse { get; init; }

    /// <summary>What tells entries that a password locks from those the user may not read, for the directory as it is now.</summary>
    public Features.Passwords.PasswordLocks? Passwords { get; init; }

    /// <summary>What tells archived folders, for the directory as it is now (none for an earlier state).</summary>
    public Features.ZippedFolders.ArchivedFolders? Archives { get; init; }

    /// <summary>How an entry of the directory as it is now is shown, by what the user may do with it.</summary>
    private ListedKind KindOf(string physicalPath, bool isDirectory)
    {
        if (tree.GetAccess(Viewer, physicalPath, isDirectory).CanRead)
            return ListedKind.Open;
        if (Passwords is null)
            return ListedKind.Locked;

        // Only what could be read without the passwords is said to be locked by one: the rest is no business of the user
        using IDisposable ignoring = Passwords.IgnoringLocks();
        return tree.GetAccess(Viewer, physicalPath, isDirectory).CanRead ? ListedKind.PasswordLocked : ListedKind.Locked;
    }

    /// <summary>Which earlier state is shown (<c>?before=</c> or <c>?commit=</c>), or null for the directory as it is.</summary>
    public EarlierStateParameter? Shown { get; init; }

    public bool ShowsEarlierState => Shown is not null;

    /// <summary>Whether it shows the changes of the commit (<c>&amp;changes</c>): only what it changed, marked, and nothing else.</summary>
    public bool ShowsChanges { get; init; }

    /// <summary>
    /// In the view of a commit, the way between its state and its changes (<see cref="ToolbarButtons.CommitChanges"/>); otherwise null.
    /// </summary>
    public ToolbarButton? CommitChangesButton { get; init; }

    /// <summary>
    /// Whether the page offers selecting entries. It needs the user's own id (the selection is kept under it), so not for someone who is not
    /// logged in, and it is for changing things, so not in an earlier state.
    /// </summary>
    public bool CanSelect => Viewer.Identity?.IsAuthenticated == true && !ShowsEarlierState;

    /// <summary>
    /// Whether downloading the directory (and the selection) asks how to make the archive first (_ArchiveModal). The archive is made for the
    /// user's own id, as the selection is kept, so it is where selecting is; elsewhere the directory is downloaded as a zip at once.
    /// </summary>
    public bool OffersArchiveOptions => CanSelect;

    /// <summary>
    /// Whether the dialog offers downloading the directory as it is kept (its own archive), for an admin in an archived folder (see
    /// <see cref="Features.Components.Archive.ArchiveHandler"/>).
    /// </summary>
    public bool OffersArchiveAsIs => OffersArchiveOptions && Viewer.IsInRole(AppAccess.AdminRole) && Archives?.ArchiveStateOf(PhysicalPath) is not null;

    /// <summary>Whether the user may manage permissions, so the page has the way to the permissions of the directory.</summary>
    public bool CanConfigure { get; init; }

    /// <summary>Whether the page has the way to the earlier states of the directory.</summary>
    public HistoryButtonState HistoryButton { get; init; } = HistoryButtonState.Hidden;

    /// <summary>
    /// In the view of a commit, the sidebar of the list of changes with what it shows from the start; otherwise null (it starts empty).
    /// </summary>
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
    /// that change things (create, upload) at all, instead of showing them disabled.
    /// </summary>
    public bool IsReadOnly => ShowsEarlierState || (CreateDeniedReason is not null && DeleteDeniedReason is not null);

    /// <summary>
    /// Whether the group of directories is there: it has some, or its button to add one. An empty group with nothing to add is left out.
    /// </summary>
    public bool ShowsDirectories => Directories.Length > 0 || !IsReadOnly;

    /// <summary>Whether the group of files is there (as <see cref="ShowsDirectories"/>).</summary>
    public bool ShowsFiles => Files.Length > 0 || !IsReadOnly;

    /// <summary>What the user has selected (anywhere) when the page is made.</summary>
    public SelectionSnapshot Selection { get; init; } = SelectionSnapshot.Empty;

    public bool IsRoot => VirtualPath == "";

    /// <summary>
    /// The path of the directory as the listing names it, which (other than <see cref="VirtualPath"/>) is the same however the address was
    /// typed.
    /// </summary>
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

        // Every entry asks about the same folders: read their configuration once, not once per entry
        using IDisposable scope = ConfigReadScope.Begin();

        // The listing knows which are directories, so the permissions needn't look at the disk for each entry
        Func<string, ListedEntry> ToListed(bool isDirectory) => entry =>
        {
            ListedKind kind = KindOf(entry, isDirectory);
            // (One a password locks shows its zipper too, after the lock)
            Features.ZippedFolders.ArchiveState? archived = kind is ListedKind.Open or ListedKind.PasswordLocked && isDirectory
                ? Archives?.ArchiveStateOf(entry)
                : null;
            // The lock only says whether its own password is unlocked; whether it is encrypted is the archive's mark (a folder can be
            // encrypted with the password of an onLowestLevel folder above, and locked for one browser while it is unzipped for another)
            return new(entry, kind)
            {
                IsUnlocked = kind == ListedKind.Open && Passwords?.HasOwnPassword(entry, isDirectory) == true,
                Archived = archived,
                ArchiveEncrypted = archived is not null && Archives?.IsEncrypted(entry) == true,
                ZipProgress = archived == Features.ZippedFolders.ArchiveState.Zipping ? Archives?.ZipProgressOf(entry) ?? 0 : 0
            };
        };
        // Anonymous users don't see the names of what they may not read at all (see AccessTree.SeesUnreadableNames), but are asked for the
        // password of what they could read with it
        bool seesUnreadable = tree.SeesUnreadableNames(Viewer);

        // With the bridges mounted here, which are directories like any other, and without what they hide or what the site doesn't show
        // (inside a shown directory that is only a name at the root that an address of the app takes)
        ChildEntry[] children = [.. locations.ListDirectory(new DirectoryInfo(PhysicalPath), ListOptions).Where(child
            => tree.IsExposed(child.Entry.FullName))];
        Directories = [..children.Where(child => child.Entry is DirectoryInfo && child.Name != PathService.ReservedName)
            .OrderByDescending(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(child => child.Entry.FullName)
            .Select(ToListed(isDirectory: true))
            .Where(entry => entry.Kind != ListedKind.Locked || seesUnreadable)];
        // The configuration file is not an entry: it is changed through the configuration page, and never listed
        ListedEntry[] files = [..MediaEntries.InListingOrder(children.Where(child => child.Entry is FileInfo && !string.Equals(child.Name,
            PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase)), child => child.Name)
            .Select(child => child.Entry.FullName)
            .Select(ToListed(isDirectory: false))
            .Where(entry => entry.Kind != ListedKind.Locked || seesUnreadable)];
        SplitOffMedia(files);
        // Markdown files are shown by their titles, which only those who may read them see; the order stays that of the file names
        Files = [.. Files.Select(file => file.CanOpen && MarkdownTitles.IsMarkdown(file.Path)
            ? file with { DisplayName = MarkdownTitles.OfFile(file.Path) }
            : file)];

        if (IsRoot)
            DeleteDeniedReason = "The root directory can't be deleted.";
        else
        {
            DeleteDeniedReason = tree.DeleteDeniedReason(Viewer, PhysicalPath);
            RenameDeniedReason = tree.RenameDeniedReason(Viewer, PhysicalPath, DeleteDeniedReason);
        }

        CreateDeniedReason = tree.CreateDeniedReason(Viewer, PhysicalPath);
    }

    /// <summary>
    /// Fills the listing with the entries of an earlier state (<see cref="Shown"/>), as <see cref="EarlierStatePermissions"/> says they are
    /// shown. Nothing in it can be changed.
    /// </summary>
    /// <param name="cantBeChanged">Why nothing of it can be changed (see <see cref="Features.HistoryViewing.HistoryTexts.CantBeChanged"/>).</param>
    public void InitFromEarlierState(string cantBeChanged, IEnumerable<(ListedEntry Entry, bool IsDirectory)> entries)
    {
        (ListedEntry Entry, bool IsDirectory)[] all = [.. entries.OrderByDescending(listed => NameOf(listed.Entry.Path),
            StringComparer.OrdinalIgnoreCase)];
        Directories = [.. all.Where(listed => listed.IsDirectory).Select(listed => listed.Entry)];
        SplitOffMedia([.. all.Where(listed => !listed.IsDirectory).Select(listed => listed.Entry)]);
        DeleteDeniedReason = RenameDeniedReason = CreateDeniedReason = cantBeChanged;
    }

    private void SplitOffMedia(ListedEntry[] files)
    {
        Files = [.. files.Where(file => !MediaEntries.IsMedia(NameOf(file.Path)))];
        Media = [.. files.Where(file => MediaEntries.IsMedia(NameOf(file.Path)))];
    }

    /// <summary>
    /// The number of an entry of a group, with as many leading zeroes as the largest number of the group has digits, so that the names
    /// start at the same place (and don't move when the group grows by a digit as entries are added).
    /// </summary>
    public static string NumberText(int index, int countInGroup)
        => (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            .PadLeft(countInGroup.ToString(System.Globalization.CultureInfo.InvariantCulture).Length, '0');

    /// <summary>Shortcut for the entry at <paramref name="index"/>: the modifiers plus 1-9, then 0. Null after the first ten.</summary>
    public static string? NumberKey(int index, string modifiers)
        => index < 10 ? modifiers + (index + 1) % 10 : null;

    public string ToVirtualPath(string s)
        => path.ToVirtualPath(s);

    /// <summary>The virtual name of the entry (physical path), which for a bridge is not that of its physical directory.</summary>
    public string NameOf(string physicalPath)
        => locations.NameOf(physicalPath);

    /// <summary>Hidden and system entries are entries like any other.</summary>
    private static readonly EnumerationOptions ListOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };
}
