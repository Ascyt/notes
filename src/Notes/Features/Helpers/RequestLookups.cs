namespace Notes.Features.Helpers;

/// <summary>
/// What the request that is being answered has looked up on disk (a folder's configuration, whether a path is a directory), so that it looks
/// each up once however often its checks ask (principles.md, 22). Outside of a request nothing is kept: every lookup goes to the disk.
/// </summary>
/// <remarks>
/// What the app changes on disk while a request runs (a configuration written, an archived folder mounted or unmounted) calls
/// <see cref="Changed"/>, after which every request looks again. While the <see cref="StructureLock"/> is held (entries are added, moved or
/// removed), nothing is kept at all: what holds it looks at the disk before and after it changes it. What
/// is changed on the server by hand counts from the next request on. Work that a request starts and that goes on beside it shares its lookups
/// while it runs, so they may be used from several threads.
/// </remarks>
public sealed class RequestLookups(IHttpContextAccessor accessor)
{
    /// <summary>How often the app changed something on disk since it started (in any of the apps of the process, which is harmless: they only look again).</summary>
    private static long _changes;

    /// <summary>How many hold the <see cref="StructureLock"/> now (at most one, but counted to be safe).</summary>
    private static int _changingEntries;

    /// <summary>Makes every request look at the disk again: called right after the app changed something there.</summary>
    public static void Changed() => Interlocked.Increment(ref _changes);

    /// <summary>From when the <see cref="StructureLock"/> is taken until <see cref="EntriesChanged"/>: nothing is looked up from what was kept.</summary>
    public static void ChangingEntries()
    {
        _ = Interlocked.Increment(ref _changingEntries);
        Changed();
    }

    public static void EntriesChanged()
    {
        Changed();
        _ = Interlocked.Decrement(ref _changingEntries);
    }

    private sealed class Known
    {
        public readonly Lock Lock = new();
        public long Changes;
        public readonly Dictionary<(string Kind, string Path), object?> Values = [];
    }

    private static readonly object ItemKey = new();

    /// <summary>
    /// What <paramref name="lookUp"/> says about the path, looked up the first time the request asks (for the kind of lookup, e.g. "config";
    /// paths in any case are the same).
    /// </summary>
    public T Get<T>(string kind, string path, Func<string, T> lookUp)
    {
        if (accessor.HttpContext is not { } context || Volatile.Read(ref _changingEntries) > 0)
            return lookUp(path);

        Known known;
        lock (ItemKey)
        {
            if (context.Items[ItemKey] is not Known existing)
                context.Items[ItemKey] = existing = new Known();
            known = existing;
        }

        (string, string) key = (kind, path.ToUpperInvariant());
        long changesBefore = Interlocked.Read(ref _changes);
        lock (known.Lock)
        {
            if (known.Changes != changesBefore)
            {
                known.Values.Clear();
                known.Changes = changesBefore;
            }
            else if (known.Values.TryGetValue(key, out object? value))
                return (T)value!;
        }

        T looked = lookUp(path);
        lock (known.Lock)
        {
            // Only what no change overtook while it was looked up
            if (known.Changes == changesBefore && Interlocked.Read(ref _changes) == changesBefore && Volatile.Read(ref _changingEntries) == 0)
                known.Values[key] = looked;
        }
        return looked;
    }

    /// <summary>Whether the path is a directory (Directory.Exists), once per request.</summary>
    public bool IsDirectory(string path) => Get("directory", Path.TrimEndingDirectorySeparator(path), Directory.Exists);
}
