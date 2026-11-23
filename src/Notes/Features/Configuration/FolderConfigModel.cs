using Notes.Features.Permissions;

namespace Notes.Features.Configuration;

// What the configuration means is specified in docs/administration/configuration.md. Keep the two in step.

/// <summary>The settings of one file of a folder, which are there by the file's name only (so also for a name that is about to be created).</summary>
/// <param name="PasswordHash">The hash of the file's password (see <see cref="Passwords.PasswordHashing"/>), or null if it has none.</param>
public sealed record FileEntry(string Name, PermsBlock Perms, string? PasswordHash = null);

/// <summary>The valid content of the configuration file of one folder.</summary>
/// <param name="Perms">The permissions of the folder, and what is in it unless it says otherwise.</param>
/// <param name="Gitignore">Whether the history leaves out the folder's files and what is below it (see <see cref="History.HistoryService"/>).</param>
/// <param name="PasswordHash">
/// The hash of the folder's password, which protects everything in it (see <see cref="Passwords.PasswordHashing"/>), or null if it has none.
/// </param>
public sealed class FolderConfig(PermsBlock perms, bool gitignore, IReadOnlyList<FileEntry> files, string? passwordHash = null)
{
    private readonly Dictionary<string, FileEntry> _files = files.ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);

    public PermsBlock Perms { get; } = perms;

    public bool Gitignore { get; } = gitignore;

    public string? PasswordHash { get; } = passwordHash;

    public IEnumerable<FileEntry> Files => _files.Values;

    public FileEntry? FindFile(string name)
        => _files.GetValueOrDefault(name);
}

/// <summary>What the configuration of a folder is.</summary>
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
