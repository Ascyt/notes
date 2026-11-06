namespace notes.Features.Helpers;

public interface IPathService
{
    /// <summary>Whether the name is usable as a single file or directory name.</summary>
    bool IsValidName(string name);
    /// <summary>The physical path of the existing file or directory at the virtual path inside <paramref name="dir"/>, or null if there is none or the path isn't valid.</summary>
    string? ToPhysicalPath(string dir, string virtualPath);
    string ToVirtualPath(string dir, string physicalPath);
}

public sealed class PathService : IPathService
{
    public string? ToPhysicalPath(string dir, string virtualPath)
    {
        // Walk down from the root one directory at a time. A segment is only followed if it names an entry that
        // actually exists in the current directory, so the result is made of paths the filesystem itself returned
        // and can never lead out of the root (no "..", drive letters, UNC paths or the like get this far).
        string current = Path.GetFullPath(dir);

        foreach (string name in virtualPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsValidName(name))
                return null;

            FileSystemInfo? entry = FindEntry(current, name, StringComparison.OrdinalIgnoreCase);
            if (entry is null || entry.LinkTarget is not null) // links could point out of the root
                return null;

            current = entry.FullName;
        }

        return current;
    }

    /// <summary>The entry called <paramref name="name"/> in the directory, or null if there is none (or the directory can't be read).</summary>
    private static FileSystemInfo? FindEntry(string directory, string name, StringComparison comparison)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFileSystemInfos()
                .FirstOrDefault(entry => string.Equals(entry.Name, name, comparison));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
