using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.Configuration;
using notes.Features.Helpers;

namespace notes.Features.Components.Transfer;

/// <summary>
/// The first stage of a transfer: finds out what exactly is to be transferred and whether it may be. Every entry (and everything
/// inside selected directories) is checked for existence, permissions and name clashes. Touches nothing, and throws a
/// <see cref="TransferException"/> for the first problem it finds. Must run under the <see cref="StructureLock"/>, together with what
/// is done with its result, so that nothing it didn't see can appear in the meantime.
/// </summary>
/// <remarks>The permissions each kind needs are specified in docs/development/security-specification.md.</remarks>
/// <remarks>
/// What is inside what, and which directory an entry is in, is asked of <see cref="NotesLocations"/>: across a bridge, the physical paths don't
/// say it. A copy may go from one physical directory to another; anything that would take a bridge away from its place is refused.
/// </remarks>
public sealed class TransferPlanner(Config config, NotesLocations locations, IPathService path, AccessTree tree)
{
    public TransferPlan Plan(ClaimsPrincipal user, TransferKind kind, string physicalTarget, IReadOnlyList<string> virtualPaths)
    {
        // Everything inside the selected directories asks about the same folders: read their configuration once
        using IDisposable scope = ConfigReadScope.Begin();
        if (kind != TransferKind.Delete)
            CheckTarget(user, physicalTarget);

        // Something inside a selected directory travels with it, so it is not an entry of its own
        List<TransferItem> selected = ResolveEntries(user, virtualPaths);
        List<TransferItem> items = [..selected.Where(item => !selected.Any(other => other.IsDirectory && IsInside(item.Source, other.Source)))];

        if (kind != TransferKind.Delete)
            items = CheckAgainstTarget(kind, physicalTarget, items);

        // Everything inside the selected directories counts as well
        bool leftOutUnreadable = false;
        List<TransferItem> planned = [];
        foreach (TransferItem item in items)
        {
            string? destination = kind == TransferKind.Delete ? null : Path.Combine(physicalTarget, item.Name);
            planned.Add(CheckTree(user, kind, Verb(kind), item, destination, ref leftOutUnreadable));
        }

        return new TransferPlan(planned, leftOutUnreadable);
    }

    /// <summary>
    /// Checks that the user may rename the (known) entry to <paramref name="destination"/>: the same as moving it there. Throws a
    /// <see cref="TransferException"/> if not. Must run under the <see cref="StructureLock"/>, together with the renaming.
    /// </summary>
    public void CheckRename(ClaimsPrincipal user, string source, string destination)
    {
        using IDisposable scope = ConfigReadScope.Begin();
        bool leftOutUnreadable = false;
        _ = CheckTree(user, TransferKind.Move, "rename", new TransferItem(source, Path.GetFileName(destination), Directory.Exists(source)), destination, ref leftOutUnreadable);
    }

    /// <summary>Whether <paramref name="inner"/> is somewhere below <paramref name="outer"/> in the notes (also across bridges).</summary>
    private bool IsInside(string inner, string outer)
        => !TransferPaths.IsSame(inner, outer) && locations.IsSameOrInside(inner, outer);

    private static string Verb(TransferKind kind)
        => kind.ToString().ToLowerInvariant();

    /// <summary>"copied", "moved", "deleted", "renamed".</summary>
    private static string Done(string verb)
        => verb == "copy" ? "copied" : verb + "d";

    private void CheckTarget(ClaimsPrincipal user, string physicalTarget)
    {
        // A directory the user can't know of is answered like one that doesn't exist. Putting something into one they may not read would
        // tell them which names are taken in it, so they must be able to open it.
        if (!Directory.Exists(physicalTarget) || !tree.IsKnown(user, physicalTarget))
            throw new TransferException(StatusCodes.Status404NotFound, "The target directory does not exist.");
        if (!tree.GetAccess(user, physicalTarget).CanRead)
            throw new TransferException(StatusCodes.Status403Forbidden, "No read access to the target directory.");
    }

    private List<TransferItem> ResolveEntries(ClaimsPrincipal user, IReadOnlyList<string> virtualPaths)
    {
        // Found all together: the selected entries are mostly in the same directory, which is then listed once instead of once per entry
        IReadOnlyList<FileSystemInfo?> found = path.FindEntries(virtualPaths);
        List<TransferItem> entries = [];
        for (int i = 0; i < virtualPaths.Count; i++)
        {
            // The user selected it, so they know the path. Whether it is gone or hidden from them now, it is answered the same.
            // (Whether they may read it is checked with the rest of it.)
            if (virtualPaths[i] == "" || found[i] is not { } entry || !tree.IsKnown(user, entry))
                throw new TransferException(StatusCodes.Status409Conflict, $"'{virtualPaths[i]}' no longer exists.");

            // (A bridge has the name of the notes, not that of its physical directory)
            entries.Add(new TransferItem(entry.FullName, locations.NameOf(entry.FullName), entry is DirectoryInfo));
        }

        return entries;
    }

    /// <summary>
    /// Makes sure the entries can go into the target directory: not into themselves, not all of them already there, no name clashes.
    /// Returns the entries that actually have to be transferred (when moving, those already in the target directory stay where they are).
    /// </summary>
    private List<TransferItem> CheckAgainstTarget(TransferKind kind, string physicalTarget, List<TransferItem> items)
    {
        foreach (TransferItem item in items)
        {
            if (item.IsDirectory && locations.IsSameOrInside(physicalTarget, item.Source))
                throw new TransferException(StatusCodes.Status400BadRequest, $"Cannot {Verb(kind)} '{item.Name}' into itself.");
        }

        if (items.All(item => TransferPaths.IsSame(locations.ParentOf(item.Source)!, physicalTarget)))
            throw new TransferException(StatusCodes.Status400BadRequest, "Everything selected is already in this directory.");

        if (kind == TransferKind.Move)
            items = [..items.Where(item => !TransferPaths.IsSame(locations.ParentOf(item.Source)!, physicalTarget))];

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (TransferItem item in items)
        {
            if (!names.Add(item.Name))
                throw new TransferException(StatusCodes.Status409Conflict, $"More than one of the selected entries is called '{item.Name}'.");
            // The target is a directory the user may read, so they can see what is taken (a bridge in it too)
            if (!locations.IsFree(Path.Combine(physicalTarget, item.Name)))
                throw new TransferException(StatusCodes.Status409Conflict, $"'{item.Name}' already exists in this directory.");
        }

        return items;
    }

    /// <summary>
    /// Checks the (known) entry and everything inside it. Stops at the first thing that may not be done. The entry itself must be readable.
    /// For a copy, entries inside it that the user may not read are left out (and <paramref name="leftOutUnreadable"/> says so); for
    /// anything else they make it fail, as they would be moved or deleted with the rest, unseen.
    /// </summary>
    /// <param name="verb">What is done, for the messages ("copy", "move", "delete", "rename").</param>
    /// <param name="destination">Physical path the entry will have afterwards; null if it goes away (delete).</param>
    /// <returns>The item, for a copy with the <see cref="TransferItem.Contents"/> to copy.</returns>
    private TransferItem CheckTree(ClaimsPrincipal user, TransferKind kind, string verb, TransferItem item, string? destination, ref bool leftOutUnreadable)
    {
        FileSystemInfo root = item.IsDirectory ? new DirectoryInfo(item.Source) : new FileInfo(item.Source);
        EntryAccessInfo rootAccess = tree.GetAccess(user, item.Source, item.IsDirectory);
        if (!rootAccess.CanRead)
            throw new TransferException(StatusCodes.Status403Forbidden, $"No read access to '{tree.Display(item.Source)}', so it can't be {Done(verb)}.");

        CheckEntry(user, kind, verb, root, rootAccess, destination);
        if (!item.IsDirectory)
            return item;

        TransferItem checkedItem = CheckContents(user, kind, verb, item, root, destination, ref leftOutUnreadable);
        // A bridge stays where the configuration puts it. Asked once everything inside is known to be readable, as the reason may name it.
        if (kind != TransferKind.Copy && tree.FixedPlaceReason(user, item.Source) is { } fixedPlace)
            throw new TransferException(StatusCodes.Status403Forbidden, fixedPlace);
        return checkedItem;
    }

    /// <summary>The part of <see cref="CheckTree"/> about what is inside the directory.</summary>
    private TransferItem CheckContents(ClaimsPrincipal user, TransferKind kind, string verb, TransferItem item, FileSystemInfo root, string? destination, ref bool leftOutUnreadable)
    {
        List<InsideEntry> contents = [];
        try
        {
            foreach (WalkedEntry walked in tree.Walk(user, (DirectoryInfo)root))
            {
                // Its name is no secret (it is listed in a directory the user may read), but what is in it is
                if (!walked.Access.CanRead)
                {
                    if (kind != TransferKind.Copy)
                        throw new TransferException(StatusCodes.Status403Forbidden,
                            $"No read access to '{tree.Display(walked.Entry.FullName)}', so '{tree.Display(item.Source)}' can't be {Done(verb)}.");

                    leftOutUnreadable = true;
                    continue;
                }

                string? entryDestination = destination is null ? null : Path.Combine(destination, walked.RelativePath);
                CheckEntry(user, kind, verb, walked.Entry, walked.Access, entryDestination);
                contents.Add(new InsideEntry(walked.Entry.FullName, walked.RelativePath, walked.Entry is DirectoryInfo));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TransferException(StatusCodes.Status500InternalServerError, $"Part of '{tree.Display(item.Source)}' can't be read by the server, so it can't be {Done(verb)}.");
        }

        return item with { Contents = contents };
    }

    private void CheckEntry(ClaimsPrincipal user, TransferKind kind, string verb, FileSystemInfo entry, EntryAccessInfo access, string? destination)
    {
        string display = tree.Display(entry.FullName);
        // Deleting a link only removes the link, so it is harmless, but copying one would follow it out of the notes
        if (kind != TransferKind.Delete && PathService.IsLink(entry))
            throw new TransferException(StatusCodes.Status400BadRequest, $"'{display}' is a link, which can't be {Done(verb)}.");

        // What is moved, renamed or deleted goes away from where it is
        if (kind != TransferKind.Copy && !access.CanDelete)
            throw new TransferException(StatusCodes.Status403Forbidden, $"Insufficient permissions to {verb} '{display}'.");

        // ...and what is copied, moved or renamed comes into existence where it goes
        if (destination is not null && !tree.GetAccess(user, destination).CanCreate)
            throw new TransferException(StatusCodes.Status403Forbidden, $"Insufficient permissions to create '{tree.Display(destination)}'.");

        // ...at an address that stays usable
        if (destination is not null && config.Limits.IsTooLong(locations.ToVirtualPath(destination)))
            throw new TransferException(StatusCodes.Status400BadRequest, config.Limits.PathTooLongMessage(tree.Display(destination)));
    }
}
