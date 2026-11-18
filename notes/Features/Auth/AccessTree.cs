using System.Security.Claims;
using notes.Features.Bridges;
using notes.Features.Configuration;
using notes.Features.Helpers;

namespace notes.Features.Auth;

/// <summary>An entry met while walking a directory, with what the user may do with it.</summary>
/// <param name="RelativePath">
/// Where it is in the directory that is walked, by the names of the notes (with the system's separator): across a bridge, its physical
/// path doesn't say that.
/// </param>
public readonly record struct WalkedEntry(FileSystemInfo Entry, EntryAccessInfo Access, string RelativePath);

/// <summary>What looking at an entry and everything inside it found out.</summary>
/// <param name="DeleteDeniedReason">Why the entry can't be deleted (or moved away, or renamed) as a whole, for the user; null if it can.</param>
public sealed record TreeAccess(string? DeleteDeniedReason)
{
    public bool CanDelete => DeleteDeniedReason is null;
}

/// <summary>
/// Applies <see cref="IEntryAccess"/> to the tree of the notes. The name of an entry is shown to every logged-in user who may read the
/// directory it is in, whatever the entry's own permissions; its contents (what is in the file, what is in the directory) only to those who
/// may read the entry itself. So what is inside a directory the user may not read is hidden from them, and nothing else is.
/// Anonymous users are the exception: for them, what they may not read is hidden as well (see <see cref="SeesUnreadableNames"/>).
/// All walks over the notes that depend on the user go through here.
/// </summary>
public sealed class AccessTree(NotesLocations locations, IPathService path, IEntryAccess access)
{
    /// <summary>Hidden and system files are part of a directory like any other, and a failure to read a directory must not go unnoticed.</summary>
    private static readonly EnumerationOptions WalkOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath)
        => access.GetAccess(user, physicalPath);

    /// <summary>For an entry the caller has just listed, so it knows whether it is a directory (see <see cref="IEntryAccess.GetAccess(ClaimsPrincipal, string, bool)"/>).</summary>
    public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath, bool isDirectory)
        => access.GetAccess(user, physicalPath, isDirectory);

    /// <summary>Whether the user may see and change the permissions themselves (the configuration of each folder and file).</summary>
    public bool CanManageConfiguration(ClaimsPrincipal user)
        => access.CanManageConfiguration(user);

    /// <summary>
    /// Whether the user is shown the names of entries they may not read (with a lock). Logged-in users are: they could find out that such
    /// an entry exists anyway, by trying to create, upload, copy, move or rename something onto its name. Anonymous users can't change
    /// anything, so they have no such way, and what they may not read is hidden from them like what is inside it.
    /// </summary>
    public bool SeesUnreadableNames(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Whether the user may know that the entry exists (see its name): they may read every directory above it, up to the root, and, if they
    /// don't <see cref="SeesUnreadableNames">see the names of unreadable entries</see>, the entry itself. Anything else is hidden and must be
    /// answered like something that doesn't exist. The root itself is always known.
    /// </summary>
    public bool IsKnown(ClaimsPrincipal user, string physicalPath)
        => CheckAncestors(user, physicalPath, includingSelf: !IsRoot(physicalPath) && !SeesUnreadableNames(user), existing: null);

    /// <summary>
    /// <see cref="IsKnown(ClaimsPrincipal, string)"/> for an entry that was just found on disk (e.g. by <see cref="IPathService.FindEntries"/>):
    /// everything above it is a directory, so the permissions needn't look at the disk for any of them.
    /// </summary>
    public bool IsKnown(ClaimsPrincipal user, FileSystemInfo existing)
        => CheckAncestors(user, existing.FullName, includingSelf: !IsRoot(existing.FullName) && !SeesUnreadableNames(user), existing);

    private bool IsRoot(string physicalPath)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath)), locations.Root.PhysicalPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the user may open the entry (see what is in it): it is <see cref="IsKnown(ClaimsPrincipal, string)">known</see>, and they may read it.</summary>
    public bool CanOpen(ClaimsPrincipal user, string physicalPath)
        => CheckAncestors(user, physicalPath, includingSelf: true, existing: null);

    /// <summary><see cref="CanOpen(ClaimsPrincipal, string)"/> for an entry that was just found on disk, see <see cref="IsKnown(ClaimsPrincipal, FileSystemInfo)"/>.</summary>
    public bool CanOpen(ClaimsPrincipal user, FileSystemInfo existing)
        => CheckAncestors(user, existing.FullName, includingSelf: true, existing);

    /// <param name="existing">
    /// The entry at the path if it was just found on disk, so it is known whether it is a directory, and that everything above it is one. If
    /// null, the permissions look at the disk for each (a path that doesn't exist has no directories above it that must exist either).
    /// </param>
    private bool CheckAncestors(ClaimsPrincipal user, string physicalPath, bool includingSelf, FileSystemInfo? existing)
    {
        string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        // What a bridge hides is no part of the notes either
        if (!locations.IsInNotes(current))
            return false;

        bool isSelf = true;
        if (!includingSelf)
        {
            if (IsRoot(current))
                return true;
            current = locations.ParentOf(current);
            isSelf = false;
        }

        // Up to the root, across the bridges (above one is the directory it is mounted in)
        while (current is not null)
        {
            EntryAccessInfo info = existing is null ? access.GetAccess(user, current)
                : access.GetAccess(user, current, isDirectory: !isSelf || existing is DirectoryInfo);
            if (!info.CanRead)
                return false;
            isSelf = false;
            if (IsRoot(current))
                return true;
            current = locations.ParentOf(current);
        }

        return false;
    }

    /// <summary>
    /// Everything inside the directory, depth first, each directory before what is in it. Entries the user may not read are returned (their
    /// names are no secret) but not entered, unless the user doesn't <see cref="SeesUnreadableNames">see such names</see>: then they are left
    /// out. Links are not entered either (they could lead out of the notes or in circles). The bridges mounted in a directory are part of it,
    /// and what they hide is not. The configuration files (<see cref="PathService.ConfigFileName"/>) are only there for those who may manage
    /// permissions: for everyone else they don't exist, so a copy or a download leaves them out.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> if a directory can't be read.
    /// </summary>
    public IEnumerable<WalkedEntry> Walk(ClaimsPrincipal user, DirectoryInfo directory)
        => Walk(user, directory, "", access.CanManageConfiguration(user), SeesUnreadableNames(user));

    private IEnumerable<WalkedEntry> Walk(ClaimsPrincipal user, DirectoryInfo directory, string relativePath, bool seesConfigs, bool seesUnreadable)
    {
        foreach ((FileSystemInfo entry, string name) in locations.ListDirectory(directory, WalkOptions))
        {
            // The folder of the app's own data (of the root, or of a bridge) is never part of the notes
            if (entry is DirectoryInfo && name == PathService.ReservedName)
                continue;
            if (!seesConfigs && entry is FileInfo && string.Equals(name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            WalkedEntry walked = new(entry, access.GetAccess(user, entry.FullName, entry is DirectoryInfo), Path.Combine(relativePath, name));
            if (!walked.Access.CanRead && !seesUnreadable)
                continue;
            yield return walked;

            if (walked.Access.CanRead && entry is DirectoryInfo subdirectory && !PathService.IsLink(subdirectory))
                foreach (WalkedEntry inner in Walk(user, subdirectory, walked.RelativePath, seesConfigs, seesUnreadable))
                    yield return inner;
        }
    }

    /// <summary>
    /// Looks at the (openable) entry and everything inside it: whether it may be deleted as a whole, which also decides whether it may be
    /// moved away or renamed. Stops at the first reason it can't. Only for what the page shows; the operations check everything themselves.
    /// </summary>
    public TreeAccess Inspect(ClaimsPrincipal user, string physicalPath)
    {
        // Every entry inside asks about the same folders: read their configuration once
        using IDisposable scope = ConfigReadScope.Begin();
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
                    return new TreeAccess($"No read access to '{Display(walked.Entry.FullName)}'.");
                if (!walked.Access.CanDelete)
                    return new TreeAccess(NotRemovable(walked.Entry.FullName));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TreeAccess("Some of what is inside can't be read by the server.");
        }

        return new TreeAccess(FixedPlaceReason(user, physicalPath));
    }

    /// <summary>
    /// Why the entry can't be deleted, moved or renamed as it is or holds a bridge, whose place the configuration of the site sets; null if
    /// it isn't and doesn't. Bridges are how the site is run: only those who manage the configuration are told so, everyone else is told
    /// what a missing permission would tell them, about the entry itself. Only to be asked once everything inside the entry is known to be
    /// readable.
    /// </summary>
    public string? FixedPlaceReason(ClaimsPrincipal user, string physicalPath)
    {
        if (locations.BridgesAtOrBelow(physicalPath).FirstOrDefault() is not { } bridge)
            return null;
        return access.CanManageConfiguration(user)
            ? $"'{bridge.VirtualPath}' is a bridge (Config:Bridges), so it can't be deleted, moved or renamed."
            : NotRemovable(physicalPath);
    }

    private string NotRemovable(string physicalPath)
        => $"Insufficient permissions to delete or move '{Display(physicalPath)}'.";

    /// <summary>
    /// Why the user can't add entries to the (openable) directory, or null if they can. Only a hint for the page: whether something may be
    /// created is up to the path it gets, which the operation itself checks. The directory's own access is taken for that of what is new
    /// in it, as new paths are expected to inherit from their directory (see docs/development/security-specification.md).
    /// </summary>
    public string? CreateDeniedReason(ClaimsPrincipal user, string physicalDirectory)
        => access.GetAccess(user, physicalDirectory).CanCreate ? null : "Insufficient permissions to add entries to this directory.";

    /// <summary>Why the user can't rename the (openable) entry that <paramref name="inspected"/> is about, or null if they can. A hint, like <see cref="CreateDeniedReason"/>.</summary>
    public string? RenameDeniedReason(ClaimsPrincipal user, string physicalPath, TreeAccess inspected)
        => inspected.DeleteDeniedReason
           ?? (access.GetAccess(user, locations.ParentOf(physicalPath)!).CanCreate
               ? null
               : "Insufficient permissions to add entries to the parent directory.");

    /// <summary>The path as the user sees it: relative to the root, with "/".</summary>
    public string Display(string physicalPath)
        => path.ToVirtualPath(physicalPath);
}
