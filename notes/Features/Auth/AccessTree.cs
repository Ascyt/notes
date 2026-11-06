using System.Security.Claims;
using notes.Features.Helpers;

namespace notes.Features.Auth;

/// <summary>An entry met while walking a directory, with what the user may do with it.</summary>
public readonly record struct WalkedEntry(FileSystemInfo Entry, EntryAccessInfo Access);

/// <summary>What looking at an entry and everything inside it found out.</summary>
/// <param name="DeleteDeniedReason">Why the entry can't be deleted (or moved away, or renamed) as a whole, for the user; null if it can.</param>
public sealed record TreeAccess(string? DeleteDeniedReason)
{
    public bool CanDelete => DeleteDeniedReason is null;
}

/// <summary>
/// Applies <see cref="IEntryAccess"/> to the tree of the notes. The name of an entry is shown to everyone who may read the directory it
/// is in, whatever the entry's own permissions; its contents (what is in the file, what is in the directory) only to those who may read
/// the entry itself. So what is inside a directory the user may not read is hidden from them, and nothing else is.
/// All walks over the notes that depend on the user go through here.
/// </summary>
public sealed class AccessTree(Config config, IPathService path, IEntryAccess access)
{
    /// <summary>Hidden and system files are part of a directory like any other, and a failure to read a directory must not go unnoticed.</summary>
    private static readonly EnumerationOptions WalkOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath)
        => access.GetAccess(user, physicalPath);

    /// <summary>
    /// Whether the user may know that the entry exists (see its name): they may read every directory above it, up to the root. Anything
    /// else is hidden and must be answered like something that doesn't exist. The root itself is always known.
    /// </summary>
    public bool IsKnown(ClaimsPrincipal user, string physicalPath)
        => CheckAncestors(user, physicalPath, includingSelf: false);

    /// <summary>Whether the user may open the entry (see what is in it): it is <see cref="IsKnown">known</see>, and they may read it.</summary>
    public bool CanOpen(ClaimsPrincipal user, string physicalPath)
        => CheckAncestors(user, physicalPath, includingSelf: true);

    private bool CheckAncestors(ClaimsPrincipal user, string physicalPath, bool includingSelf)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.Dir));
        string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        if (!IsSameOrInside(current, root))
            return false;

        if (!includingSelf)
        {
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                return true;
            current = Path.GetDirectoryName(current);
        }

        while (current is not null)
        {
            if (!access.GetAccess(user, current).CanRead)
                return false;
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                return true;
            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    /// <summary>
    /// Everything inside the directory, depth first, each directory before what is in it. Entries the user may not read are returned (their
    /// names are no secret) but not entered, and neither are links (they could lead out of the notes or in circles).
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> if a directory can't be read.
    /// </summary>
    public IEnumerable<WalkedEntry> Walk(ClaimsPrincipal user, DirectoryInfo directory)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos("*", WalkOptions))
        {
            // The folder for globally saved data is never part of the notes (only the root has one)
            if (entry is DirectoryInfo && entry.Name == PathService.ReservedName)
                continue;

            WalkedEntry walked = new(entry, access.GetAccess(user, entry.FullName));
            yield return walked;

            if (walked.Access.CanRead && entry is DirectoryInfo { LinkTarget: null } subdirectory)
                foreach (WalkedEntry inner in Walk(user, subdirectory))
                    yield return inner;
        }
    }

    /// <summary>
    /// Looks at the (openable) entry and everything inside it: whether it may be deleted as a whole, which also decides whether it may be
    /// moved away or renamed. Stops at the first reason it can't. Only for what the page shows; the operations check everything themselves.
    /// </summary>
    public TreeAccess Inspect(ClaimsPrincipal user, string physicalPath)
    {
        if (!access.GetAccess(user, physicalPath).CanDelete)
            return new TreeAccess(NotRemovable(physicalPath));
        if (!Directory.Exists(physicalPath))
            return new TreeAccess(null);

        try
        {
            foreach (WalkedEntry walked in Walk(user, new DirectoryInfo(physicalPath)))
            {
                // What can't be read can't be moved or deleted either: what is in it would go along unseen
                if (!walked.Access.CanRead)
                    return new TreeAccess($"You may not open '{Display(walked.Entry.FullName)}'.");
                if (!walked.Access.CanDelete)
                    return new TreeAccess(NotRemovable(walked.Entry.FullName));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TreeAccess("Some of what is inside can't be read by the server.");
        }

        return new TreeAccess(null);
    }

    private string NotRemovable(string physicalPath)
        => $"You may not delete or move '{Display(physicalPath)}'.";

    /// <summary>
    /// Why the user can't add entries to the (openable) directory, or null if they can. Only a hint for the page: whether something may be
    /// created is up to the path it gets, which the operation itself checks. The directory's own access is taken for that of what is new
    /// in it, as new paths are expected to inherit from their directory (see docs/security-specification.md).
    /// </summary>
    public string? CreateDeniedReason(ClaimsPrincipal user, string physicalDirectory)
        => access.GetAccess(user, physicalDirectory).CanCreate ? null : "You may not add entries to this directory.";

    /// <summary>Why the user can't rename the (openable) entry that <paramref name="inspected"/> is about, or null if they can. A hint, like <see cref="CreateDeniedReason"/>.</summary>
    public string? RenameDeniedReason(ClaimsPrincipal user, string physicalPath, TreeAccess inspected)
        => inspected.DeleteDeniedReason
           ?? (access.GetAccess(user, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(physicalPath))!).CanCreate
               ? null
               : "You may not add entries to the directory it is in.");

    /// <summary>The path as the user sees it: relative to the root, with "/".</summary>
    public string Display(string physicalPath)
        => path.ToVirtualPath(config.Dir, physicalPath);

    private static bool IsSameOrInside(string child, string parent)
        => string.Equals(child, parent, StringComparison.OrdinalIgnoreCase)
           || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
