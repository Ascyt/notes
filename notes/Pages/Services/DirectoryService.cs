namespace notes.Pages.Services;

public sealed class DirectoryService(Config config, IPathService path) : IDirectoryService
{
    public string CreateEntry(string physicalDirectory, bool isFile)
    {
        string baseName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
        string extension = isFile ? ".md" : "";

        string name = baseName + extension;
        for (int n = 1; Path.Exists(Path.Combine(physicalDirectory, name)); n++)
            name = $"{baseName}_{n}{extension}";

        string newPhysicalPath = Path.Combine(physicalDirectory, name);
        if (isFile)
            File.WriteAllText(newPhysicalPath, "");
        else
            Directory.CreateDirectory(newPhysicalPath);

        return path.ToVirtualPath(config.Dir, newPhysicalPath);
    }

    public void Delete(string physicalPath)
        => Directory.Delete(physicalPath, recursive: true);

    public string? Rename(string physicalPath, string newName)
    {
        string newPhysicalPath = Path.Combine(Path.GetDirectoryName(physicalPath)!, newName);
        if (Path.Exists(newPhysicalPath) && !string.Equals(newPhysicalPath, physicalPath, StringComparison.OrdinalIgnoreCase))
            return null;

        Directory.Move(physicalPath, newPhysicalPath);
        return path.ToVirtualPath(config.Dir, newPhysicalPath);
    }
}

public interface IDirectoryService
{
    /// <summary>Renames the directory and returns its new virtual path, or null if the name is already taken.</summary>
    string? Rename(string physicalPath, string newName);

    /// <summary>Deletes the directory and everything in it.</summary>
    void Delete(string physicalPath);

    /// <summary>Creates a timestamp-named file or directory and returns its virtual path.</summary>
    string CreateEntry(string physicalDirectory, bool isFile);
}
