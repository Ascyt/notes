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
}

public interface IPathService
{
    string ToPhysicalPath(string dir, string virtualPath);
    string ToVirtualPath(string dir, string physicalPath);
}