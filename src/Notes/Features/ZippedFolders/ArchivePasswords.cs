using System.Collections.Concurrent;
using Notes.Features.Configuration;
using Notes.Features.Passwords;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// The passwords of folders whose settings make archives (an archived folder's own, or an <c>onLowestLevel</c> one's), in memory only, from
/// when someone unlocks one until no folder needs it any more (they are zipped, encrypted, again). An encrypted archive is unzipped and
/// zipped with it; it is never written anywhere. After a restart it is gone: an encrypted folder that was unzipped then stays unzipped until
/// someone unlocks it again (see docs/development/zipped-folders.md).
/// </summary>
/// <remarks>
/// Each is kept with the hash it was found right for. A password is only used while the configuration still has that hash, so a password
/// that was changed meanwhile is never used to encrypt (which would make the archive one nobody can open).
/// </remarks>
public sealed class ArchivePasswords(FolderConfigStore configs) : IPasswordWitness
{
    private sealed record Known(string Password, string Hash);

    private readonly ConcurrentDictionary<string, Known> _known = new(StringComparer.OrdinalIgnoreCase);

    public void Verified(Protection protection, string password)
    {
        // Only those that archives need: every other password stays where it was typed
        if (configs.Get(protection.PhysicalPath).Config?.OwnArchive is { Enabled: true })
            Remember(protection.PhysicalPath, password, protection.Hash);
    }

    /// <summary>Keeps the password of the folder (physical), which is right for the hash.</summary>
    public void Remember(string folder, string password, string hash)
        => _known[Normalize(folder)] = new Known(password, hash);

    /// <summary>
    /// The password of the folder (physical), if it is known and its configuration still has the hash it was found right for; null otherwise.
    /// </summary>
    public string? PasswordOf(string folder)
        => _known.TryGetValue(Normalize(folder), out Known? known) && configs.Get(folder).Config?.PasswordHash == known.Hash
            ? known.Password
            : null;

    public bool Knows(string folder) => PasswordOf(folder) is not null;

    public void Forget(string folder) => _known.TryRemove(Normalize(folder), out _);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
