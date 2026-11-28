using Notes.Features.Components.Selection;

namespace Notes.Features.Passwords;

/// <summary>
/// An entry with a password of its own (a directory's in its configuration, a file's in the entry of its folder's), with the hash it has.
/// Whatever is inside a protected directory is protected by it too.
/// </summary>
/// <param name="PhysicalPath">The entry that has the password (full, without a separator at the end).</param>
public sealed record Protection(string PhysicalPath, string Hash);

/// <summary>
/// What each browser has unlocked, in memory (lost on a restart, like the selections): a random id in a cookie of its own leads to the
/// protections it typed the password of, each for <see cref="PasswordsConfig.UnlockMinutes"/> after it was last used. An unlock is for the
/// path and the hash together, so a new password locks the entry again, and so does moving or renaming it. Neither the password nor
/// anything made of it is kept.
/// </summary>
/// <remarks>
/// An unlock is also tied to the user who made it (or to nobody, for someone not logged in): the cookie of a browser in which someone else
/// logs in later unlocks nothing for them.
/// </remarks>
public sealed class PasswordUnlocks(PasswordsConfig settings, TimeProvider time)
{
    public const string CookieName = "notes-unlocks";

    private sealed class Unlocked(string? userId)
    {
        public string? UserId { get; } = userId;

        /// <summary>When each protection was last used.</summary>
        public Dictionary<Protection, DateTimeOffset> LastUsed { get; } = new(ProtectionComparer.Instance);
    }

    private readonly Dictionary<string, Unlocked> _byBrowser = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    private TimeSpan Lifetime => TimeSpan.FromMinutes(settings.UnlockMinutes);

    /// <summary>Whether the browser of the request has the protection unlocked; if it has, it stays so for another <see cref="Lifetime"/>.</summary>
    public bool IsUnlocked(HttpContext context, Protection protection)
    {
        lock (_lock)
        {
            if (Of(context) is not { } unlocked || !unlocked.LastUsed.TryGetValue(protection, out DateTimeOffset lastUsed))
                return false;

            DateTimeOffset now = time.GetUtcNow();
            if (now - lastUsed >= Lifetime)
            {
                _ = unlocked.LastUsed.Remove(protection);
                return false;
            }

            unlocked.LastUsed[protection] = now;
            return true;
        }
    }

    /// <summary>
    /// What the browser of the request has unlocked, to be asked after the request ended (work it started that runs on, an archive that is
    /// made): as it is then, with the same lifetime (and using an unlock keeps it, as for a request). Nothing if the request has no unlocks.
    /// </summary>
    public Func<Protection, bool> ForLater(HttpContext context)
    {
        if (context.Request.Cookies[CookieName] is not { } id)
            return _ => false;
        string? userId = SelectionHandler.UserIdOf(context.User);
        return protection =>
        {
            lock (_lock)
            {
                if (!_byBrowser.TryGetValue(id, out Unlocked? unlocked) || unlocked.UserId != userId
                    || !unlocked.LastUsed.TryGetValue(protection, out DateTimeOffset lastUsed))
                    return false;
                DateTimeOffset now = time.GetUtcNow();
                if (now - lastUsed >= Lifetime)
                {
                    _ = unlocked.LastUsed.Remove(protection);
                    return false;
                }
                unlocked.LastUsed[protection] = now;
                return true;
            }
        };
    }

    /// <summary>Whether the browser of the request has anything unlocked (so that the page offers to lock it again).</summary>
    public bool HasAnyUnlocked(HttpContext context)
    {
        lock (_lock)
        {
            DateTimeOffset now = time.GetUtcNow();
            return Of(context)?.LastUsed.Values.Any(lastUsed => now - lastUsed < Lifetime) == true;
        }
    }

    /// <summary>Unlocks the protection for the browser of the request, which gets the cookie for it if it has none yet.</summary>
    public void Unlock(HttpContext context, Protection protection)
    {
        lock (_lock)
        {
            RemoveExpired();
            string? userId = SelectionHandler.UserIdOf(context.User);
            if (Of(context) is not { } unlocked)
            {
                string id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                unlocked = new Unlocked(userId);
                _byBrowser[id] = unlocked;
                context.Response.Cookies.Append(CookieName, id, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Strict,
                    // Without it, the entries could never be unlocked. (No expiry: it goes with the browser session, the unlocks much sooner.)
                    IsEssential = true
                });
            }

            unlocked.LastUsed[protection] = time.GetUtcNow();
        }
    }

    /// <summary>
    /// The entry at <paramref name="from"/> (and what is in it) is now at <paramref name="to"/> (renamed): what the browser of the request had
    /// unlocked there is unlocked where it is now.
    /// </summary>
    public void TreeMoved(HttpContext context, string from, string to)
    {
        lock (_lock)
        {
            if (Of(context) is not { } unlocked)
                return;

            from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from));
            to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(to));
            foreach ((Protection protection, DateTimeOffset lastUsed) in unlocked.LastUsed.ToList())
            {
                bool isSame = string.Equals(protection.PhysicalPath, from, StringComparison.OrdinalIgnoreCase);
                if (!isSame && !protection.PhysicalPath.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                _ = unlocked.LastUsed.Remove(protection);
                unlocked.LastUsed[protection with { PhysicalPath = to + protection.PhysicalPath[from.Length..] }] = lastUsed;
            }
        }
    }

    /// <summary>
    /// Locks the protection for the browser of the request: it is asked for its password again (an encrypted archive needs the password itself,
    /// which the app may have forgotten while the browser still had it unlocked).
    /// </summary>
    public void Lock(HttpContext context, Protection protection)
    {
        lock (_lock)
            _ = Of(context)?.LastUsed.Remove(protection);
    }

    /// <summary>The protections the browser of the request has unlocked now.</summary>
    public List<Protection> UnlockedBy(HttpContext context)
    {
        lock (_lock)
        {
            DateTimeOffset now = time.GetUtcNow();
            return Of(context) is { } unlocked ? [.. unlocked.LastUsed.Where(pair => now - pair.Value < Lifetime).Select(pair => pair.Key)] : [];
        }
    }

    /// <summary>Locks everything the browser of the request has unlocked.</summary>
    public void LockAll(HttpContext context)
    {
        lock (_lock)
        {
            if (context.Request.Cookies[CookieName] is { } id)
                _ = _byBrowser.Remove(id);
        }

        context.Response.Cookies.Delete(CookieName);
    }

    /// <summary>What the cookie of the request leads to, if it is the current user's.</summary>
    private Unlocked? Of(HttpContext context)
        => context.Request.Cookies[CookieName] is { } id && _byBrowser.TryGetValue(id, out Unlocked? unlocked)
           && unlocked.UserId == SelectionHandler.UserIdOf(context.User)
            ? unlocked
            : null;

    /// <summary>Forgets what ran out, so that browsers that never come back don't fill the memory.</summary>
    private void RemoveExpired()
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach ((string id, Unlocked unlocked) in _byBrowser.ToList())
        {
            foreach (Protection protection in unlocked.LastUsed.Where(pair => now - pair.Value >= Lifetime).Select(pair => pair.Key).ToList())
                _ = unlocked.LastUsed.Remove(protection);
            if (unlocked.LastUsed.Count == 0)
                _ = _byBrowser.Remove(id);
        }
    }

    /// <summary>Paths are the same in any case, as everywhere in the app; hashes are compared exactly.</summary>
    private sealed class ProtectionComparer : IEqualityComparer<Protection>
    {
        public static ProtectionComparer Instance { get; } = new();

        public bool Equals(Protection? x, Protection? y)
            => x is not null && y is not null && string.Equals(x.PhysicalPath, y.PhysicalPath, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Hash, y.Hash, StringComparison.Ordinal);

        public int GetHashCode(Protection obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PhysicalPath), StringComparer.Ordinal.GetHashCode(obj.Hash));
    }
}
