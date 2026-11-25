using Microsoft.AspNetCore.Mvc.RazorPages;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Breadcrumb;

/// <summary>
/// A directory above the entry that an earlier state is shown of, which can't be seen as it was itself: <paramref name="Reason"/> says why.
/// It leads to the directory as it is now, whose page says so too, unless it isn't there now (<paramref name="ExistsNow"/>).
/// </summary>
public sealed record ParentWithoutHistory(string Reason, bool ExistsNow);

public class _Breadcrumb : PageModel
{
    /// <summary>The styles and the script of the breadcrumb, by their paths in the library (see <see cref="Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Components/Breadcrumb/_Breadcrumb.css";
    public const string Script = "Features/Components/Breadcrumb/_Breadcrumb.js";

    /// <summary>Whether the current entry is a file (otherwise a directory), used for the icon.</summary>
    public bool IsFile { get; init; }

    public string VirtualPath { get; init; } = "";

    /// <summary>Where the breadcrumb starts (<see cref="BreadcrumbStarts.Of"/>): <see cref="RootVirtualPath"/>, and the links before it.</summary>
    public BreadcrumbStart Start { get; init; } = BreadcrumbStart.AtTheRoot;

    /// <summary>
    /// The directory the site shows that the entry is in, or is (<see cref="Addresses.ExposedFolders.RootOf"/>), "" where it shows all of
    /// the entries. Nothing above it is part of the site.
    /// </summary>
    public string RootVirtualPath => Start.RootVirtualPath;

    /// <summary>The links of the site shown before the start (<c>Config:BreadcrumbParents</c>).</summary>
    public IReadOnlyList<BreadcrumbParent> VirtualParents => Start.VirtualParents;

    /// <summary>Why the current entry can't be renamed, or null if it can (see <see cref="Rename._RenameInput.DisabledReason"/>).</summary>
    public string? RenameDisabledReason { get; init; }

    /// <summary>
    /// In an earlier state: the directories above that can't be seen as they were, by virtual path (<see cref="RootVirtualPath"/> for where the
    /// breadcrumb starts, in any case), shown with a crossed-out calendar like such entries of a listing.
    /// </summary>
    public IReadOnlyDictionary<string, ParentWithoutHistory> ParentsWithoutHistory { get; init; } = new Dictionary<string, ParentWithoutHistory>();

    /// <summary>The names below where the breadcrumb starts, down to the entry.</summary>
    private string[] Names => VirtualPath[RootVirtualPath.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);

    public bool IsRoot => Names.Length == 0;

    /// <summary>The name of the directory the breadcrumb starts at, or null where that is the root (<see cref="BreadcrumbTexts.Root"/>).</summary>
    public string? RootDirectoryName => RootVirtualPath == "" ? null : RootVirtualPath.Split('/')[^1];

    public string RootAddress => VirtualPaths.ToAddress(RootVirtualPath);

    /// <summary>Name of the current entry, shown as the rename input.</summary>
    public string Name => IsRoot ? "" : Names[^1];

    /// <summary>The icon before the name (<see cref="Pages.Browse.EntryIcons"/>).</summary>
    public string Icon => IsFile ? Pages.Browse.EntryIcons.OfFile(Name) : Pages.Browse.EntryIcons.Directory;

    /// <summary>Virtual path of the parent directory (<see cref="RootVirtualPath"/> for one right below the start), or null at the start.</summary>
    public string? ParentVirtualPath => IsRoot ? null : Below(Names[..^1]);

    /// <summary>Each parent of the current entry below the start, with the virtual path it links to.</summary>
    public IEnumerable<(string Name, string Path)> Parents
    {
        get
        {
            string[] names = Names;
            for (int i = 0; i < names.Length - 1; i++)
                yield return (names[i], Below(names[..(i + 1)]));
        }
    }

    /// <summary>
    /// The entries along the way (the start, the parents, the entry itself) with a password of their own that is unlocked, by virtual path: each
    /// gets an open lock after its name. Of the entries as they are now, so nothing for a path that isn't there now.
    /// </summary>
    public HashSet<string> UnlockedPaths(Passwords.PasswordLocks locks, IPathService paths)
    {
        HashSet<string> unlocked = new(StringComparer.OrdinalIgnoreCase);
        if (paths.ToPhysicalPath(VirtualPath) is not { } physicalPath)
            return unlocked;
        foreach (string protectedPath in locks.UnlockedAlong(physicalPath, isDirectory: !IsFile))
            _ = unlocked.Add(paths.ToVirtualPath(protectedPath) is var virtualPath and not "." ? virtualPath : "");
        return unlocked;
    }

    /// <summary>Whether the entry the breadcrumb ends with is in <paramref name="unlocked"/> (see <see cref="UnlockedPaths"/>).</summary>
    public bool IsUnlocked(HashSet<string> unlocked)
        => unlocked.Contains(VirtualPath.Trim('/'));

    /// <summary>
    /// The archived folders along the way (the parents, and the entry itself if it is a directory), by virtual path, with how each is kept
    /// now: each gets the zipper (or the shield, if encrypted) after its name. Of the entries as they are now.
    /// </summary>
    public Dictionary<string, ZippedFolders._ArchivedMark> ArchivedPaths(ZippedFolders.ArchivedFolders archives, IPathService paths)
    {
        Dictionary<string, ZippedFolders._ArchivedMark> archived = new(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> along = Parents.Select(parent => parent.Path);
        if (!IsFile && !IsRoot)
            along = along.Append(VirtualPath.Trim('/'));
        foreach (string virtualPath in along)
            if (paths.ToPhysicalPath(virtualPath) is { } physical && Directory.Exists(physical)
                && archives.ArchiveStateOf(physical) is { } state)
                archived[virtualPath] = new ZippedFolders._ArchivedMark { State = state, Encrypted = archives.IsEncrypted(physical) };
        return archived;
    }

    /// <summary>The virtual path of the names below the start.</summary>
    private string Below(string[] names)
        => RootVirtualPath == "" ? string.Join('/', names) : string.Join('/', [RootVirtualPath, .. names]);
}
