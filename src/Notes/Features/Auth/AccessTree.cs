using System.Security.Claims;
using Notes.Features.Addresses;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.Helpers;

namespace Notes.Features.Auth;

/// <summary>An entry met while walking a directory, with what the user may do with it.</summary>
/// <param name="RelativePath">
/// Where it is in the directory that is walked, by the names of the entries (with the system's separator): across a bridge, its physical
/// path doesn't say that.
/// </param>
public readonly record struct WalkedEntry(FileSystemInfo Entry, EntryAccessInfo Access, string RelativePath);

/// <summary>
/// Applies <see cref="IEntryAccess"/> to the tree of the entries. The name of an entry is shown to every logged-in user who may read the
/// directory it is in, whatever the entry's own permissions; its contents (what is in the file, what is in the directory) only to those who
/// may read the entry itself. So what is inside a directory the user may not read is hidden from them, and nothing else is.
/// Anonymous users are the exception: for them, what they may not read is hidden as well (see <see cref="SeesUnreadableNames"/>).
/// All walks over the entries that depend on the user go through here.
/// </summary>
public sealed class AccessTree(EntryLocations locations, IPathService path, IEntryAccess access, ExposedFolders exposed, AuthTexts texts)
{
    /// <summary>
    /// Whether the site shows the entry (physical path of an entry, see <see cref="ExposedFolders"/>). What it doesn't show is hidden from
    /// everyone; the directories above an exposed entry still count for whether it is known, exposed or not.
    /// </summary>
    public bool IsExposed(string physicalPath)
        => exposed.IsExposed(locations.ToVirtualPath(physicalPath));

    /// <summary>Hidden and system files are part of a directory like any other, and a failure to read a directory must not go unnoticed.</summary>
    private static readonly EnumerationOptions WalkOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath)
        => access.GetAccess(user, physicalPath);

    /// <summary>
    /// For an entry the caller has just listed, so it knows whether it is a directory (see <see cref="IEntryAccess.GetAccess(ClaimsPrincipal,
    /// string, bool)"/>).
    /// </summary>
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
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath)), locations.Root.PhysicalPath,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the user may open the entry (see what is in it): it is <see cref="IsKnown(ClaimsPrincipal, string)">known</see>, and they may
    /// read it.
    /// </summary>
    public bool CanOpen(ClaimsPrincipal user, string physicalPath)
        => CheckAncestors(user, physicalPath, includingSelf: true, existing: null);

    /// <summary>
    /// <see cref="CanOpen(ClaimsPrincipal, string)"/> for an entry that was just found on disk, see <see cref="IsKnown(ClaimsPrincipal,
    /// FileSystemInfo)"/>.
    /// </summary>
    public bool CanOpen(ClaimsPrincipal user, FileSystemInfo existing)
        => CheckAncestors(user, existing.FullName, includingSelf: true, existing);

    /// <param name="existing">
    /// The entry at the path if it was just found on disk, so it is known whether it is a directory, and that everything above it is one. If
    /// null, the permissions look at the disk for each (a path that doesn't exist has no directories above it that must exist either).
    /// </param>
    private bool CheckAncestors(ClaimsPrincipal user, string physicalPath, bool includingSelf, FileSystemInfo? existing)
    {
        string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        // What a bridge hides is no part of the entries either, and what the site doesn't show is no part of it
        if (!locations.Contains(current) || !IsExposed(current))
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
    /// out. Links are not entered either (they could lead out of the root or in circles). The bridges mounted in a directory are part of it,
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
            // The folder of the app's own data (of the root, or of a bridge) is never part of the entries
            if (entry is DirectoryInfo && name == PathService.ReservedName)
                continue;
            if (!seesConfigs && entry is FileInfo && string.Equals(name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            // (Everything inside an exposed directory is exposed, but at the root an address of the app may take a name)
            if (!IsExposed(entry.FullName))
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
    /// Why the user may not delete the (openable) entry itself, which also keeps them from moving it away or renaming it; null if they may.
    /// Only a hint for the page, and an optimistic one: what is inside a directory isn't looked at, as walking a large tree for every page
    /// would make it slow. Deleting, moving and renaming check everything inside themselves, and say why when they can't.
    /// </summary>
    public string? DeleteDeniedReason(ClaimsPrincipal user, string physicalPath)
        => !access.GetAccess(user, physicalPath).CanDelete ? NotRemovable(physicalPath)
            // (An exposed directory itself is known to be one without looking inside; one deeper down is found when it is done)
            : exposed.IsOrHoldsExposedFolder(locations.ToVirtualPath(physicalPath)) && !exposed.ExposesEverything ? FixedPlaceReason(user,
                physicalPath)
            : null;

    /// <summary>
    /// Why the entry can't be deleted, moved or renamed as it is or holds a bridge or an exposed directory, whose places the configuration of
    /// the site sets; null if it isn't and doesn't. They are how the site is run: only those who manage the configuration are told so,
    /// everyone else is told what a missing permission would tell them, about the entry itself. Only to be asked once everything inside the
    /// entry is known to be readable.
    /// </summary>
    public string? FixedPlaceReason(ClaimsPrincipal user, string physicalPath)
    {
        string virtualPath = locations.ToVirtualPath(physicalPath);
        string? why = locations.BridgesAtOrBelow(physicalPath).FirstOrDefault() is { } bridge
            ? texts.IsABridge(bridge.VirtualPath)
            : !exposed.ExposesEverything && exposed.IsOrHoldsExposedFolder(virtualPath)
                ? texts.HoldsAnExposedDirectory(virtualPath)
                : null;
        if (why is null)
            return null;
        return access.CanManageConfiguration(user) ? why : NotRemovable(physicalPath);
    }

    private string NotRemovable(string physicalPath)
        => texts.CantDeleteOrMove(Display(physicalPath));

    /// <summary>
    /// Why the user can't add entries to the (openable) directory, or null if they can. Only a hint for the page: whether something may be
    /// created is up to the path it gets, which the operation itself checks. The directory's own access is taken for that of what is new
    /// in it, as new paths are expected to inherit from their directory (see docs/development/security-specification.md).
    /// </summary>
    public string? CreateDeniedReason(ClaimsPrincipal user, string physicalDirectory)
        => access.GetAccess(user, physicalDirectory).CanCreate ? null : texts.CantAddHere;

    /// <summary>
    /// Why the user can't rename the (openable) entry, or null if they can, given why they can't delete it (<see cref="DeleteDeniedReason"/>).
    /// A hint as optimistic as that one.
    /// </summary>
    public string? RenameDeniedReason(ClaimsPrincipal user, string physicalPath, string? deleteDeniedReason)
        => deleteDeniedReason
           ?? (access.GetAccess(user, locations.ParentOf(physicalPath)!).CanCreate
               ? null
               : texts.CantAddToParent);

    /// <summary>The path as the user sees it: relative to the root, with "/".</summary>
    public string Display(string physicalPath)
        => path.ToVirtualPath(physicalPath);
}
