using System.Text;

namespace notes.Pages.Services;

public sealed class FileService(Config config, IPathService path) : IFileService
{
    public async Task SaveAsync(string physicalPath, Stream body)
    {
        using StreamReader reader = new(body, Encoding.UTF8);
        string contents = await reader.ReadToEndAsync();
        await File.WriteAllTextAsync(physicalPath, contents);
    }

    public void Delete(string physicalPath)
        => File.Delete(physicalPath);

    public string? Rename(string physicalPath, string newName)
    {
        string newPhysicalPath = Path.Combine(Path.GetDirectoryName(physicalPath)!, newName);
        if (Path.Exists(newPhysicalPath) && !string.Equals(newPhysicalPath, physicalPath, StringComparison.OrdinalIgnoreCase))
            return null;

        File.Move(physicalPath, newPhysicalPath);
        return path.ToVirtualPath(config.Dir, newPhysicalPath);
    }
}

public interface IFileService
{
    /// <summary>Renames the file and returns its new virtual path, or null if the name is already taken.</summary>
    string? Rename(string physicalPath, string newName);

    void Delete(string physicalPath);
    Task SaveAsync(string physicalPath, Stream body);
}
