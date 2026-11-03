namespace notes.Pages.Services;

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

    public bool IsValidName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name == name.Trim()
           && name is not ("." or "..")
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && !name.Contains('\\');
}

public interface IPathService
{
    /// <summary>Whether the name is usable as a single file or directory name.</summary>
    bool IsValidName(string name);
    /// <summary>The physical path of the existing file or directory at the virtual path inside <paramref name="dir"/>, or null if there is none or the path isn't valid.</summary>
    string? ToPhysicalPath(string dir, string virtualPath);
    string ToVirtualPath(string dir, string physicalPath);
}