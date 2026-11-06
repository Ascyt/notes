using System.Security.Claims;
using notes.Features.Helpers;
using notes.Features.Permissions;

namespace notes.Features.Auth;

// What the permissions mean, and which operation needs which, is specified in docs/security-specification.md. Keep the two in step.
// Where they come from (the configuration files) is specified in docs/permissions.md.

/// <summary>
/// What may be done with the contents of an entry: seeing them, and changing them. Each level includes the one before. The entry's name
/// is not part of it: that is seen by whoever may read the directory it is in.
/// </summary>
public enum ReadAccess
{
    /// <summary>
    /// The entry is listed by its name, but can't be opened, downloaded, selected, copied, moved, renamed or deleted, and copies and downloads
    /// of the directories it is in leave it out. What is inside a directory like this is hidden completely.
    /// </summary>
    None,

    /// <summary>The entry may be opened, downloaded, selected and copied.</summary>
    Read,

    /// <summary>Also the contents of the file may be changed (saved). Means nothing more than <see cref="Read"/> for a directory.</summary>
    ReadAndWrite,
}

/// <summary>Whether an entry may come into existence at its path, and go away from it again. Each level includes the one before.</summary>
public enum CreateAccess
{
    /// <summary>Nothing may be created at the path, and what is there may not be removed.</summary>
    None,

    /// <summary>An entry may be created at the path (new file or directory, upload, destination of a copy, move or rename).</summary>
    Create,

    /// <summary>Also the entry at the path may be removed (deleted, or the source of a move or rename).</summary>
    CreateAndDelete,
}

/// <summary>Everything a user may do with one path. More may be added here; it is all found out by a single <see cref="IEntryAccess.GetAccess"/>.</summary>
public sealed record EntryAccessInfo(ReadAccess Read, CreateAccess Create)
{
    public static EntryAccessInfo None { get; } = new(ReadAccess.None, CreateAccess.None);

    /// <summary>The contents may be seen. Whether the entry can be opened also depends on the directories above it, see <see cref="AccessTree.CanOpen"/>.</summary>
    public bool CanRead => Read >= ReadAccess.Read;

    public bool CanWrite => Read >= ReadAccess.ReadAndWrite;

    public bool CanCreate => Create >= CreateAccess.Create;

    public bool CanDelete => Create >= CreateAccess.CreateAndDelete;
}

/// <summary>
/// The single place that decides what a user may do with a path inside the notes. Everything else asks here (mostly through
/// <see cref="AccessTree"/>), so restricting files or directories only means changing the implementation.
/// </summary>
public interface IEntryAccess
{
    /// <summary>
    /// What the user may do with the path. The path need not exist: asking for one that doesn't is how the app finds out whether something
    /// may be created there. Must answer the same for the same user and path whether or not something exists there.
    /// </summary>
    EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath);

    /// <summary>
    /// Whether the user may see and change the permissions themselves (the configuration of each folder and file) rather than just
    /// live by them. Answered once for the whole notes: it is not up to the entry.
    /// </summary>
    bool CanManagePermissions(ClaimsPrincipal user);
}

/// <summary>
/// Decides from the configuration file of each folder (docs/permissions.md). Admins may do anything; for everyone else the permissions
/// come from the nearest entries that say anything about them, looking at the file's own entry first, then at its folder, then at the folders
/// above it, going up only past entries with <c>inherit: true</c> (the first one with <c>inherit: false</c> is the last one looked at). What nothing says anything about is denied, and so is everything under a folder whose
/// configuration is invalid.
/// </summary>
public sealed class EntryAccess(Config config, FolderConfigStore store) : IEntryAccess
{
    private static readonly EntryAccessInfo Everything = new(ReadAccess.ReadAndWrite, CreateAccess.CreateAndDelete);

    public bool CanManagePermissions(ClaimsPrincipal user)
        => user.IsInRole(NotesAccess.AdminRole);

    public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath)
    {
        if (CanManagePermissions(user))
            return Everything;

        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.Dir));
        // The configuration files themselves are for admins only
        if (string.Equals(Path.GetFileName(path), PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase)
            || !IsSameOrInside(path, root))
            return EntryAccessInfo.None;

        PrincipalSet principals = PrincipalSet.Of(user);
        if (FindLayers(path, root) is not { } layers)
            return EntryAccessInfo.None;

        // Each level is decided by the first layer that says anything about the user
        ReadAccess read = layers.Select(perms => perms.DecideRead(principals)).FirstOrDefault(decision => decision is not null) ?? ReadAccess.None;
        CreateAccess create = layers.Select(perms => perms.DecideCreate(principals)).FirstOrDefault(decision => decision is not null) ?? CreateAccess.None;
        return new EntryAccessInfo(read, create);
    }

    /// <summary>
    /// The permissions that apply to the path, the most specific first: the entry of the file (a directory has none, its own folder is
    /// its entry), then the folder and each folder above it, stopping after the first one with <c>inherit: false</c>. Null if one of those is invalid.
    /// </summary>
    private List<PermsBlock>? FindLayers(string path, string root)
    {
        List<PermsBlock> layers = [];
        string folder = path;

        // A directory is its own folder. Any other path (a file, or one that doesn't exist yet) has the entry its folder has for its name.
        if (!Directory.Exists(path))
        {
            folder = Path.GetDirectoryName(path)!;
            FolderConfigState parent = store.Get(folder);
            if (parent.IsInvalid)
                return null;

            if (parent.Config?.FindFile(Path.GetFileName(path)) is { } entry)
            {
                layers.Add(entry.Perms);
                if (!entry.Perms.Inherit)
                    return layers;
            }
        }

        while (true)
        {
            FolderConfigState state = store.Get(folder);
            if (state.IsInvalid)
                return null;

            if (state.Config is not null)
            {
                layers.Add(state.Config.Perms);
                if (!state.Config.Perms.Inherit)
                    return layers;
            }

            if (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(folder) is not { } above)
                return layers;
            folder = above;
        }
    }

    private static bool IsSameOrInside(string child, string parent)
        => string.Equals(child, parent, StringComparison.OrdinalIgnoreCase)
           || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Ends an operation that the user may not do. The message is shown to them, so it must not name anything inside a directory they may not read.</summary>
public sealed class AccessDeniedException(string message) : Exception(message);
