namespace notes.Features.Permissions;

/// <summary>
/// For the length of one synchronous operation that asks about many paths (walking a directory, planning a transfer), the configuration of each
/// folder is read from disk once instead of once per path: <see cref="FolderConfigStore.Get"/> looks at the file's timestamp every time, and
/// a walk asks for every folder above every entry. Within the scope the operation sees the configuration as it was when it first looked, which
/// is also what makes its answers consistent with each other.
/// </summary>
/// <remarks>
/// Only for code that doesn't wait (no <c>await</c> between <see cref="Begin"/> and disposing the scope), and that changes no configuration
/// while it is open. Scopes nest: an inner one joins the outer one.
/// </remarks>
public static class ConfigReadScope
{
    private static readonly AsyncLocal<Dictionary<string, FolderConfigState>?> Current = new();

    private static readonly IDisposable Joined = new JoinedScope();

    public static IDisposable Begin()
    {
        if (Current.Value is not null)
            return Joined;

        Current.Value = new Dictionary<string, FolderConfigState>(StringComparer.OrdinalIgnoreCase);
        return new OpenScope();
    }

    internal static bool TryGet(string physicalDirectory, out FolderConfigState state)
    {
        state = FolderConfigState.Missing;
        return Current.Value is { } known && known.TryGetValue(physicalDirectory, out state!);
    }

    internal static void Remember(string physicalDirectory, FolderConfigState state)
    {
        if (Current.Value is { } known)
            known[physicalDirectory] = state;
    }

    private sealed class OpenScope : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }

    private sealed class JoinedScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
