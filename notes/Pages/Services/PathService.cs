namespace notes.Pages.Services;

public sealed class PathService : IPathService
{
    public string ToPhysicalPath(string dir, string virtualPath)
    {
        string a = SlashesToOsSpecific(Path.GetFullPath(dir));
        string b = SlashesToOsSpecific(virtualPath.TrimStart('/'));
        return Path.Combine(a, b);
        
        string SlashesToOsSpecific(string s)
            => s.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
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
    string ToPhysicalPath(string dir, string virtualPath);
    string ToVirtualPath(string dir, string physicalPath);
}