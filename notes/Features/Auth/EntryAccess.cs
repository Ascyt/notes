using System.Security.Claims;

namespace notes.Features.Auth;

// What the permissions mean, and which operation needs which, is specified in docs/security-specification.md. Keep the two in step.

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
}

/// <summary>For now admins may do anything and nobody else anything at all.</summary>
public sealed class EntryAccess : IEntryAccess
{
    private static readonly EntryAccessInfo Everything = new(ReadAccess.ReadAndWrite, CreateAccess.CreateAndDelete);

    public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath)
        => user.IsInRole(NotesAccess.AdminRole) ? Everything : EntryAccessInfo.None;
}

/// <summary>Ends an operation that the user may not do. The message is shown to them, so it must not name anything inside a directory they may not read.</summary>
public sealed class AccessDeniedException(string message) : Exception(message);
