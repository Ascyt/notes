namespace notes.Features.Helpers;

public interface IPathService
{
    /// <summary>Whether the name is usable as a single file or directory name.</summary>
    bool IsValidName(string name);
    /// <summary>The physical path of the existing file or directory at the virtual path inside <paramref name="dir"/>, or null if there is none or the path isn't valid.</summary>
    string? ToPhysicalPath(string dir, string virtualPath);

    /// <summary>
    /// The existing entries at the virtual paths, each found like <see cref="ToPhysicalPath"/> finds it (null where that gives null; the root for
    /// an empty path). A directory that several of the paths go through is listed only once, so many paths in the same directory (a selection)
    /// cost about as much as one: on a slow disk, listing a directory again for every path is what takes the time.
    /// </summary>
    IReadOnlyList<FileSystemInfo?> FindEntries(string dir, IReadOnlyList<string> virtualPaths);

    string ToVirtualPath(string dir, string physicalPath);
}

public sealed class PathService : IPathService
{
    /// <summary>Hidden and system entries are entries like any other, and a directory that can't be read must not look empty.</summary>
    private static readonly EnumerationOptions ListOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    public string? ToPhysicalPath(string dir, string virtualPath)
        => Resolve(dir, virtualPath, FindEntry)?.FullName;

    public IReadOnlyList<FileSystemInfo?> FindEntries(string dir, IReadOnlyList<string> virtualPaths)
    {
        // What was found in each directory, by name, for all of the paths
        Dictionary<string, Listing> listings = new(StringComparer.Ordinal);
        Dictionary<(string Directory, string Name), FileSystemInfo?> found = [];

        FileSystemInfo? FindListed(string directory, string name)
        {
            if (found.TryGetValue((directory, name), out FileSystemInfo? known))
                return known;

            if (!listings.TryGetValue(directory, out Listing? listing))
                listings[directory] = listing = Listing.Of(directory);

            // Looked at for links only once, not for every path that goes through it
            FileSystemInfo? entry = listing.Find(name);
            return found[(directory, name)] = entry is null || IsLink(entry) ? null : entry;
        }

        return [..virtualPaths.Select(virtualPath => Resolve(dir, virtualPath, FindListed, linksChecked: true))];
    }

    /// <summary>
    /// Walks down from the root one directory at a time. A segment is only followed if it names an entry that actually exists in the current
    /// directory (as <paramref name="find"/> finds it), so the result is made of paths the filesystem itself returned and can never lead out
    /// of the root (no "..", drive letters, UNC paths or the like get this far). Links are never followed: they could point out of the root.
    /// </summary>
    /// <param name="linksChecked">Whether <paramref name="find"/> already leaves out links.</param>
    private FileSystemInfo? Resolve(string dir, string virtualPath, Func<string, string, FileSystemInfo?> find, bool linksChecked = false)
    {
        FileSystemInfo current = new DirectoryInfo(Path.GetFullPath(dir));

        foreach (string name in virtualPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsValidName(name))
                return null;

            FileSystemInfo? entry = find(current.FullName, name);
            if (entry is null || (!linksChecked && IsLink(entry)))
                return null;

            current = entry;
        }

        return current;
    }

    /// <summary>
    /// Whether the entry (as a listing returned it) is a link, which is never followed: it could lead out of the notes. A link always has the
    /// reparse point attribute, which the listing already knows; only then is the link itself looked at (on Windows, other things have that
    /// attribute too, e.g. files kept in the cloud). Asking every entry for its link target would cost a call to the disk for each.
    /// If the entry can't be looked at, it counts as a link.
    /// </summary>
    public static bool IsLink(FileSystemInfo entry)
    {
        try
        {
            return (entry.Attributes & FileAttributes.ReparsePoint) != 0 && entry.LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// The entry called <paramref name="name"/> in the directory, or null if there is none (or the directory can't be read). Names are
    /// compared ignoring case; where the filesystem has several that only differ in case, the one spelled exactly the same wins.
    /// </summary>
    private static FileSystemInfo? FindEntry(string directory, string name)
    {
        try
        {
            // The name as a pattern: the directory is still read through, but only what matches becomes an entry. (A name with * or ? in it
            // matches more than itself, so what matches is compared once more.)
            EnumerationOptions options = new() { AttributesToSkip = 0, IgnoreInaccessible = false, MatchType = MatchType.Simple, MatchCasing = MatchCasing.CaseInsensitive };
            FileSystemInfo? caseless = null;
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos(name, options))
            {
                if (entry.Name == name)
                    return entry;
                if (caseless is null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    caseless = entry;
            }

            return caseless;
        }
        catch (ArgumentException)
        {
            // A name the enumeration refuses as a pattern
            return Listing.Of(directory).Find(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Everything in one directory, by name. Empty if the directory can't be read (or isn't one).</summary>
    private sealed class Listing
    {
        private readonly Dictionary<string, FileSystemInfo> _exact = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FileSystemInfo> _caseless = new(StringComparer.OrdinalIgnoreCase);

        public static Listing Of(string directory)
        {
            Listing listing = new();
            try
            {
                foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", ListOptions))
                {
                    listing._exact[entry.Name] = entry;
                    _ = listing._caseless.TryAdd(entry.Name, entry);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new Listing();
            }

            return listing;
        }

        /// <summary>Like <see cref="FindEntry"/>: ignoring case, but the exact spelling first.</summary>
        public FileSystemInfo? Find(string name)
            => _exact.GetValueOrDefault(name) ?? _caseless.GetValueOrDefault(name);
    }

    public string ToVirtualPath(string dir, string physicalPath)
    {
        string s = Path.GetRelativePath(Path.GetFullPath(dir), physicalPath);
        return s.Replace('\\', '/');
    }

    /// <summary>
    /// Name of the folder in the notes directory for globally saved data (temp files and the like). It is
    /// not a valid name, so no path, upload or rename can ever lead to it, and listings skip it.
    /// </summary>
    public const string ReservedName = "_";

    /// <summary>
    /// Name of the file in every directory that holds its permissions (see docs/permissions.md). It is never listed, and not a valid name, so
    /// no path, upload, create or rename can ever lead to it: only the permissions editor reads and writes it.
    /// </summary>
    public const string ConfigFileName = ".config.json";

    public bool IsValidName(string name)
        => IsValid(name);

    /// <summary>The rules of <see cref="IsValidName"/>, for those that have no service at hand.</summary>
    /// <remarks>
    /// Windows drops dots (and spaces) at the end of a name, so there "x." is another spelling of "x" (and ".config.json." one of the
    /// configuration file). Such names are refused everywhere, so that what a name means doesn't depend on where the notes are kept.
    /// </remarks>
    public static bool IsValid(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name == name.Trim()
           && !name.EndsWith('.')
           && name is not ("." or ".." or ReservedName)
           && !string.Equals(name, ConfigFileName, StringComparison.OrdinalIgnoreCase)
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && !name.Contains('\\');
}
