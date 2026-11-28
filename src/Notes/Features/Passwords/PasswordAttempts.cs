using Notes.Features.Components.Selection;

namespace Notes.Features.Passwords;

/// <summary>
/// Slows down guessing: for each user (or, for someone not logged in, each address) and protected entry, the wrong passwords in a row are
/// counted. The first <see cref="PasswordsConfig.FreeAttempts"/> are answered at once; after that, each needs a wait since the last wrong
/// one, doubling from a second up to <see cref="PasswordsConfig.MaxWaitSeconds"/>. A right password ends the count. Someone who mistypes a few
/// times never notices; a program that guesses gets about one try a minute. Kept in memory: a restart forgets it.
/// </summary>
public sealed class PasswordAttempts(PasswordsConfig settings, TimeProvider time)
{
    /// <summary>How long a count is kept without a new wrong password, after which it starts over.</summary>
    private static readonly TimeSpan ForgottenAfter = TimeSpan.FromHours(1);

    private sealed record Count(int WrongInARow, DateTimeOffset LastWrong);

    private readonly Dictionary<string, Count> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>How long the requester has to wait before a password for the protected entry is checked; zero if not at all.</summary>
    public TimeSpan WaitBeforeNext(HttpContext context, Protection protection)
    {
        lock (_lock)
        {
            if (!_counts.TryGetValue(KeyOf(context, protection), out Count? count) || count.WrongInARow < settings.FreeAttempts)
                return TimeSpan.Zero;

            double seconds = Math.Min(Math.Pow(2, count.WrongInARow - settings.FreeAttempts), settings.MaxWaitSeconds);
            TimeSpan left = count.LastWrong + TimeSpan.FromSeconds(seconds) - time.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public void Wrong(HttpContext context, Protection protection)
    {
        lock (_lock)
        {
            DateTimeOffset now = time.GetUtcNow();
            // (Now and then, so that requesters who never come back don't fill the memory)
            if (_counts.Count > 1000)
                foreach (string stale in _counts.Where(pair => now - pair.Value.LastWrong >= ForgottenAfter).Select(pair => pair.Key).ToList())
                    _ = _counts.Remove(stale);

            string key = KeyOf(context, protection);
            int before = _counts.TryGetValue(key, out Count? count) && now - count.LastWrong < ForgottenAfter ? count.WrongInARow : 0;
            _counts[key] = new Count(before + 1, now);
        }
    }

    public void Right(HttpContext context, Protection protection)
    {
        lock (_lock)
            _ = _counts.Remove(KeyOf(context, protection));
    }

    private static string KeyOf(HttpContext context, Protection protection)
    {
        string requester = SelectionHandler.UserIdOf(context.User) is { } userId ? "u:" + userId
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "");
        return requester + "|" + protection.PhysicalPath;
    }
}
