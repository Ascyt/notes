namespace notes.Pages.Services;

public sealed class PathService : IPathService
{
    public string GetPhysicalPath(string dir, string virtualPath)
    {
        string a = SlashesToOsSpecific(Path.GetFullPath(dir));
        string b = SlashesToOsSpecific(virtualPath.TrimStart('/'));
        return Path.Combine(a, b);

        string SlashesToOsSpecific(string s)
            => s.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}

public interface IPathService
{
    string GetPhysicalPath(string dir, string virtualPath);
}