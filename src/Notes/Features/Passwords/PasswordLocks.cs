using System.Text;
using Notes.Features.Bridges;
using Notes.Features.Configuration;

namespace Notes.Features.Passwords;

/// <summary>
/// Decides whether a path is locked by a password for the request that is being answered: the entry or a directory above it has a password
/// (<see cref="ProtectionsOf"/>), and the browser hasn't unlocked it (<see cref="PasswordUnlocks"/>), nor does the request bring it along
/// in the <see cref="HeaderName"/> header (for programs, which shouldn't have to go through the dialog). A locked path is one nobody may
/// read or change, admins included: <see cref="Auth.EntryAccess"/> asks here before anything else.
/// </summary>
/// <remarks>
/// Outside of a request (a background job) nothing is unlocked. The page of an entry asks whether the user may know of it as if nothing
/// were locked (<see cref="IgnoringLocks"/>), so that it can ask for the password instead of answering as if the entry weren't there.
/// </remarks>
public sealed class PasswordLocks(PasswordUnlocks unlocks, PasswordAttempts attempts, FolderConfigStore store, EntryLocations locations,
    IHttpContextAccessor accessor)
{
    /// <summary>The header in which a request may bring passwords along, one in each (a request may need several), each in base64 of its UTF-8.</summary>
    public const string HeaderName = "X-Notes-Password";

    /// <summary>How many of the passwords a request brings are checked at most (each check takes a while, on purpose).</summary>
    private const int MaxHeaderChecks = 16;

    private static readonly AsyncLocal<int> IgnoringDepth = new();

    private sealed class HeaderChecks
    {
        public Dictionary<Protection, bool> Results { get; } = [];
        public int Count { get; set; }

        /// <summary>How long the requester has to wait before a password they brought along can be checked, if one couldn't be.</summary>
        public TimeSpan? Wait { get; set; }
    }

    /// <summary>Until the result is disposed, nothing counts as locked (on this flow of the request only).</summary>
    public IDisposable IgnoringLocks()
    {
        IgnoringDepth.Value++;
        return new Ending(() => IgnoringDepth.Value--);
    }

    /// <summary>
    /// The protections that apply to the path, the outermost first: those of the directories above it (across bridges) and the entry's own (a
    /// directory's in its configuration, a file's, or a path that isn't there yet, in the entry of its folder). A configuration that isn't
    /// valid says nothing here (only admins get anything there anyway).
    /// </summary>
    /// <param name="configOf">The configuration of a folder (physical path): as it is now, or as it was (an earlier state).</param>
    public List<Protection> ProtectionsOf(string physicalPath, Func<string, FolderConfigState> configOf, bool isDirectory)
    {
        List<Protection> protections = [];
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        if (!locations.Contains(path))
            return protections;

        // The entry's own, if it is a file: in the entry of its folder
        string? folder = path;
        if (!isDirectory)
        {
            folder = locations.ParentOf(path);
            if (folder is not null && configOf(folder).Config?.FindFile(Path.GetFileName(path))?.PasswordHash is { } fileHash)
                protections.Add(new Protection(path, fileHash));
        }

        // Then the folder's and each one's above, up to the root
        for (; folder is not null; folder = locations.ParentOf(folder))
            if (configOf(folder).Config?.PasswordHash is { } hash)
                protections.Add(new Protection(folder, hash));

        protections.Reverse();
        return protections;
    }

    /// <summary>Whether the path is locked for the request that is being answered (see <see cref="ProtectionsOf"/> for the parameters).</summary>
    public bool IsLocked(string physicalPath, Func<string, FolderConfigState> configOf, bool isDirectory)
        => IgnoringDepth.Value == 0 && ProtectionsOf(physicalPath, configOf, isDirectory).Any(protection => !IsUnlocked(protection));

    /// <summary>
    /// The outermost protection that locks the entry as it is now, or null. What is above the path counts whether or not the path exists; its
    /// own password only if it does (a leftover entry of a file that is gone tells nothing).
    /// </summary>
    public Protection? FirstLocked(string physicalPath)
    {
        bool isDirectory = Directory.Exists(physicalPath);
        bool exists = isDirectory || File.Exists(physicalPath);
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        return ProtectionsOf(path, store.Get, isDirectory)
            .Where(protection => exists || !string.Equals(protection.PhysicalPath, path, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(protection => !IsUnlocked(protection));
    }

    /// <summary>Whether the entry has a password of its own (not only one of a directory above), as it is now.</summary>
    public bool HasOwnPassword(string physicalPath, bool isDirectory)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        if (isDirectory)
            return store.Get(path).Config?.PasswordHash is not null;
        return locations.ParentOf(path) is { } folder
               && store.Get(folder).Config?.FindFile(Path.GetFileName(path))?.PasswordHash is not null;
    }

    /// <summary>
    /// The entries along the path (it, and the directories above it) whose password the request has unlocked, as it is now: those an open lock
    /// marks.
    /// </summary>
    public List<string> UnlockedAlong(string physicalPath, bool isDirectory)
        => [.. ProtectionsOf(physicalPath, store.Get, isDirectory).Where(IsUnlocked).Select(protection => protection.PhysicalPath)];

    /// <summary>Whether the request that is being answered has the protection unlocked (also by bringing the password along).</summary>
    public bool IsUnlocked(Protection protection)
        => accessor.HttpContext is { } context && IsUnlocked(context, protection);

    public bool IsUnlocked(HttpContext context, Protection protection)
        => unlocks.IsUnlocked(context, protection) || BringsPasswordOf(context, protection);

    /// <summary>
    /// An entry with a password moved (or was copied, renamed): if the browser of the request had it unlocked where it was, it has it unlocked
    /// where it is now too, instead of being asked again right after.
    /// </summary>
    public void Moved(Protection from, string toPhysicalPath)
    {
        if (accessor.HttpContext is { } context && IsUnlocked(context, from))
            unlocks.Unlock(context, new Protection(Path.TrimEndingDirectorySeparator(Path.GetFullPath(toPhysicalPath)), from.Hash));
    }

    /// <summary>
    /// How long the request has to wait before a password it brought along in the header can be checked, if one couldn't be because of too
    /// many wrong ones; null otherwise.
    /// </summary>
    public TimeSpan? WaitForHeaderPassword(HttpContext context)
        => context.Items[typeof(HeaderChecks)] is HeaderChecks checks ? checks.Wait : null;

    /// <summary>
    /// Whether one of the passwords in the header is that of the protection. Each protection is checked once per request, and every wrong one
    /// counts towards the wait (<see cref="PasswordAttempts"/>), as if it was typed into the dialog.
    /// </summary>
    private bool BringsPasswordOf(HttpContext context, Protection protection)
    {
        List<string> passwords = HeaderPasswordsOf(context);
        if (passwords.Count == 0)
            return false;

        if (context.Items[typeof(HeaderChecks)] is not HeaderChecks checks)
            context.Items[typeof(HeaderChecks)] = checks = new HeaderChecks();
        if (checks.Results.TryGetValue(protection, out bool known))
            return known;
        if (checks.Count >= MaxHeaderChecks)
            return false;

        TimeSpan wait = attempts.WaitBeforeNext(context, protection);
        if (wait > TimeSpan.Zero)
        {
            checks.Wait = wait;
            return false;
        }

        checks.Count++;
        bool right = passwords.Any(password => PasswordHashing.Verify(password, protection.Hash));
        checks.Results[protection] = right;
        if (right)
            attempts.Right(context, protection);
        else
            attempts.Wrong(context, protection);
        return right;
    }

    /// <summary>
    /// The passwords in the header. Each is base64 of its UTF-8 bytes, as in Basic authentication: a header can't carry other characters
    /// reliably. (Base64 has no commas, so values that were joined into one on the way are split again.) What isn't base64 of UTF-8 is left out.
    /// </summary>
    private static List<string> HeaderPasswordsOf(HttpContext context)
    {
        List<string> passwords = [];
        UTF8Encoding strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        foreach (string? value in context.Request.Headers[HeaderName])
            foreach (string part in (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (passwords.Count >= MaxHeaderChecks)
                    return passwords;
                // (Longer than the longest password can be, encoded: not decoded at all)
                if (part.Length > 4 * PasswordHashing.MaxBytes)
                    continue;
                byte[] bytes = new byte[part.Length];
                if (!Convert.TryFromBase64String(part, bytes, out int length))
                    continue;
                try
                {
                    passwords.Add(strict.GetString(bytes, 0, length));
                }
                catch (DecoderFallbackException) { }
            }
        return passwords;
    }

    private sealed class Ending(Action end) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (!_ended)
                end();
            _ended = true;
        }
    }
}
