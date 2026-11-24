using Notes.Features.Components.Archive;
using Notes.Features.Permissions;

namespace Notes.Features.Configuration;

// What the configuration means is specified in docs/administration/configuration.md. Keep the two in step.

/// <summary>The settings of one file of a folder, which are there by the file's name only (so also for a name that is about to be created).</summary>
/// <param name="PasswordHash">The hash of the file's password (see <see cref="Passwords.PasswordHashing"/>), or null if it has none.</param>
public sealed record FileEntry(string Name, PermsBlock Perms, string? PasswordHash = null);

/// <summary>
/// Whether a folder is kept in an archive (<c>_.7z</c> or <c>_.zip</c> in it), which is unzipped while it is used (see
/// docs/development/zipped-folders.md), and how it is made.
/// </summary>
/// <param name="Compression">As the archive is written; for a 7z never <see cref="ArchiveCompression.Store"/>, which it can't do.</param>
/// <param name="Encrypted">Whether it is encrypted with the folder's password (not supported yet).</param>
/// <param name="OnLowestLevel">
/// Whether only the lowest folders below (those that hold files) are archived, with these settings, instead of this one (see
/// <see cref="ZippedFolders.ArchiveSettingsResolver"/>).
/// </param>
public sealed record ArchiveSettings(bool Enabled, ArchiveFormat Format, ArchiveCompression Compression, bool Encrypted, bool OnLowestLevel)
{
    /// <summary>What a folder whose configuration doesn't say has: not archived.</summary>
    public static ArchiveSettings Disabled { get; } = new(false, ArchiveFormat.SevenZip, ArchiveCompression.High, false, false);
}

/// <summary>The valid content of the configuration file of one folder.</summary>
/// <param name="Perms">The permissions of the folder, and what is in it unless it says otherwise.</param>
/// <param name="Gitignore">Whether the history leaves out the folder's files and what is below it (see <see cref="History.HistoryService"/>).</param>
/// <param name="PasswordHash">
/// The hash of the folder's password, which protects everything in it (see <see cref="Passwords.PasswordHashing"/>), or null if it has none.
/// </param>
/// <param name="Archive">
/// Whether the folder is kept in an archive, as its own configuration says; null if it doesn't say (<c>"archive": null</c>, or none): not
/// archived, unless a folder above archives it (<c>onLowestLevel</c>). Only <see cref="ZippedFolders.ArchiveSettingsResolver"/> tells which.
/// </param>
public sealed class FolderConfig(PermsBlock perms, bool gitignore, IReadOnlyList<FileEntry> files, string? passwordHash = null,
    ArchiveSettings? archive = null)
{
    /// <summary>The <c>archive</c> block as written, or null if there is none (or it is null).</summary>
    public ArchiveSettings? OwnArchive { get; } = archive;

    private readonly Dictionary<string, FileEntry> _files = files.ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);

    public PermsBlock Perms { get; } = perms;

    public bool Gitignore { get; } = gitignore;

    public string? PasswordHash { get; } = passwordHash;

    /// <summary><see cref="OwnArchive"/>, or <see cref="ArchiveSettings.Disabled"/> if there is none.</summary>
    public ArchiveSettings Archive { get; } = archive ?? ArchiveSettings.Disabled;

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
