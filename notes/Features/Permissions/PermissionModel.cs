using System.Runtime.CompilerServices;
using System.Security.Claims;
using notes.Features.Auth;

namespace notes.Features.Permissions;

// What the configuration means, and how it is resolved, is specified in docs/administration/permissions.md. Keep the two in step.

/// <summary>
/// Who a permission is for. Written in the configuration as <c>u:name</c> (a user), <c>g:name</c> (a Keycloak group) or <c>r:name</c> (a
/// realm role); this is the one place that knows how those are spelled and how they are compared.
/// </summary>
public static class Principal
{
    public const int MaxLength = 256;

    /// <summary>
    /// Written <c>a</c>: all users, logged in or not. So a user who is logged in never gets less than one who is not. It can't be given
    /// anything that changes the notes, as those who are not logged in may not change anything, see <see cref="PermissionParser"/>.
    /// </summary>
    public const string Everyone = "a";

    /// <summary>Written <c>l</c>: all users who are logged in, so that a group that has everyone in it needn't be made in Keycloak.</summary>
    public const string LoggedIn = "l";

    /// <summary>
    /// The canonical spelling of a principal as written in the configuration: user names are not case sensitive (Keycloak makes them
    /// lower case), group and role names are. Returns null (with the reason) if it isn't a principal.
    /// </summary>
    public static string? Normalize(string text, out string? error)
    {
        error = null;
        if (text is Everyone or LoggedIn)
            return text;

        if (text.Length < 3 || text[1] != ':' || text[0] is not ('u' or 'g' or 'r'))
        {
            error = $"'{text}' is not a principal. Write u:<user name>, g:<group>, r:<role>, a for all users or l for all users who are logged in.";
            return null;
        }

        string name = text[2..];
        if (name != name.Trim() || name.Any(char.IsControl))
        {
            error = $"'{text}' must not have spaces around the name, or control characters in it.";
            return null;
        }

        if (text.Length > MaxLength)
        {
            error = $"'{text[..32]}...' is too long.";
            return null;
        }

        return text[0] == 'u' ? "u:" + name.ToLowerInvariant() : text;
    }
}

/// <summary>
/// Everything that identifies a user for the permissions: their user name and id, their groups and their realm roles (all from the
/// claims of the session, see <see cref="NotesClaims"/>), each spelled as <see cref="Principal"/> does.
/// </summary>
public sealed class PrincipalSet
{
    private static readonly ConditionalWeakTable<ClaimsPrincipal, PrincipalSet> Cache = new();

    private readonly HashSet<string> _values;

    private PrincipalSet(HashSet<string> values)
    {
        _values = values;
    }

    /// <summary>Whether the user is any of those in the list.</summary>
    public bool IsAnyOf(IReadOnlySet<string> principals)
        => principals.Count > 0 && principals.Overlaps(_values);

    public static PrincipalSet Of(ClaimsPrincipal user)
        => Cache.GetValue(user, Build);

    private static PrincipalSet Build(ClaimsPrincipal user)
    {
        // Someone who is not logged in is only "everyone", whatever claims they may carry. Someone who is logged in is that and "logged in" as well.
        HashSet<string> values = new(StringComparer.Ordinal) { Principal.Everyone };
        if (user.Identity?.IsAuthenticated != true)
            return new PrincipalSet(values);

        _ = values.Add(Principal.LoggedIn);

        foreach (Claim claim in user.Claims)
        {
            string? prefix = claim.Type switch
            {
                NotesClaims.UserName or NotesClaims.UserId => "u:",
                NotesClaims.Groups => "g:",
                NotesClaims.Roles => "r:",
                _ => null
            };
            if (prefix is not null && Principal.Normalize(prefix + claim.Value, out _) is { } principal)
                values.Add(principal);
        }

        return new PrincipalSet(values);
    }
}

/// <summary>
/// The permissions one configuration block gives: six lists of principals, and whether what the block says nothing about is decided by the
/// block above it. The lists are the spellings of <see cref="Principal.Normalize"/>.
/// </summary>
/// <param name="Inherit">Whether, for a principal none of the lists mention, the permissions of the folder (for a folder: the one above it) apply.</param>
public sealed record PermsBlock(
    bool Inherit,
    IReadOnlySet<string> RwAllow,
    IReadOnlySet<string> RwReadOnly,
    IReadOnlySet<string> RwDeny,
    IReadOnlySet<string> CdAllow,
    IReadOnlySet<string> CdCreateOnly,
    IReadOnlySet<string> CdDeny)
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    /// <summary>What a block without any list says: nothing, and the block above decides.</summary>
    public static PermsBlock Inheriting { get; } = new(true, None, None, None, None, None, None);

    /// <summary>
    /// What the block says about reading and writing for the user, or null if it says nothing about them. A deny beats an allow, and an
    /// allow beats read-only (for a user who is in several of the lists through different groups).
    /// </summary>
    public ReadAccess? DecideRead(PrincipalSet user)
    {
        if (user.IsAnyOf(RwDeny))
            return ReadAccess.None;
        if (user.IsAnyOf(RwAllow))
            return ReadAccess.ReadAndWrite;
        return user.IsAnyOf(RwReadOnly) ? ReadAccess.Read : null;
    }

    /// <summary>What the block says about creating and deleting for the user, or null if it says nothing about them. Decided like <see cref="DecideRead"/>.</summary>
    public CreateAccess? DecideCreate(PrincipalSet user)
    {
        if (user.IsAnyOf(CdDeny))
            return CreateAccess.None;
        if (user.IsAnyOf(CdAllow))
            return CreateAccess.CreateAndDelete;
        return user.IsAnyOf(CdCreateOnly) ? CreateAccess.Create : null;
    }
}

/// <summary>The permissions of one file of a folder, which are there by the file's name only (so also for a name that is about to be created).</summary>
public sealed record FileEntry(string Name, PermsBlock Perms);

/// <summary>The valid content of the configuration file of one folder.</summary>
/// <param name="Perms">The permissions of the folder, and what is in it unless it says otherwise.</param>
public sealed class FolderConfig(PermsBlock perms, IReadOnlyList<FileEntry> files)
{
    private readonly Dictionary<string, FileEntry> _files = files.ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);

    public PermsBlock Perms { get; } = perms;

    public FileEntry? FindFile(string name)
        => _files.GetValueOrDefault(name);
}

/// <summary>What the configuration of a folder is, as far as the permissions are concerned.</summary>
/// <param name="Config">The configuration, if there is a valid one.</param>
/// <param name="Errors">Why the file that is there can't be used; empty if it is valid or there is none.</param>
public sealed record FolderConfigState(FolderConfig? Config, IReadOnlyList<string> Errors)
{
    /// <summary>There is no configuration file: the folder says nothing, and the one above it decides.</summary>
    public static FolderConfigState Missing { get; } = new(null, []);

    /// <summary>
    /// There is a configuration file, but it is not valid. Nobody but admins gets anything wherever it applies: a folder is never left
    /// open because someone broke its file.
    /// </summary>
    public bool IsInvalid => Errors.Count > 0;
}
