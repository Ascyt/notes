using System.IO.Compression;

namespace notes.Pages.Services;

public interface IDirectoryService
{
    /// <summary>Renames the directory and returns its new virtual path, or null if the name is already taken.</summary>
    string? Rename(string physicalPath, string newName);

    /// <summary>Deletes the directory and everything in it.</summary>
    void Delete(string physicalPath);

    /// <summary>Creates a timestamp-named file or directory and returns its virtual path.</summary>
    string CreateEntry(string physicalDirectory, bool isFile);

    /// <summary>
    /// Writes the contents of the directory as a zip (smallest size) to <paramref name="output"/>. Asynchronous all the way,
    /// so a slow client holds no thread while the zip waits for it.
    /// </summary>
    Task WriteZipAsync(string physicalDirectory, Stream output, CancellationToken cancellationToken);
}

public sealed class DirectoryService(Config config, IPathService path) : IDirectoryService
{
    public async Task WriteZipAsync(string physicalDirectory, Stream output, CancellationToken cancellationToken)
    {
        DeferredWriteStream target = new(output);
        await using (ZipArchive zip = await ZipArchive.CreateAsync(target, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
            await AddDirectoryAsync(zip, physicalDirectory, "", cancellationToken);

        // The zip's last bytes (closing the final entry, the central directory) may have been written synchronously
        await target.FlushDeferredAsync(cancellationToken);
    }

    /// <param name="prefix">Zip path of the directory: "" for the top level, otherwise ending in "/".</param>
    private static async Task AddDirectoryAsync(ZipArchive zip, string physicalDirectory, string prefix, CancellationToken cancellationToken)
    {
        // Same rules as the listing and the path service: nothing that is a link (it could point out of the notes) and no reserved folder
        FileSystemInfo[] entries = [..new DirectoryInfo(physicalDirectory).EnumerateFileSystemInfos()
            .Where(entry => entry.LinkTarget is null && !(entry is DirectoryInfo && entry.Name == PathService.ReservedName))];

        // Keeps empty directories, which only exist in a zip as an entry of their own
        if (entries.Length == 0 && prefix != "")
            zip.CreateEntry(prefix);

        foreach (FileSystemInfo entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry is DirectoryInfo directory)
                await AddDirectoryAsync(zip, directory.FullName, prefix + directory.Name + "/", cancellationToken);
            else
                await AddFileAsync(zip, (FileInfo)entry, prefix + entry.Name, cancellationToken);
        }
    }

    private static async Task AddFileAsync(ZipArchive zip, FileInfo file, string entryName, CancellationToken cancellationToken)
    {
        // A note being edited at the moment is still read
        await using FileStream source = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);

        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);
        // Zip can only store times from 1980 to 2107
        DateTimeOffset lastWrite = file.LastWriteTime;
        entry.LastWriteTime = lastWrite.Year is < 1980 or > 2107 
            ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, lastWrite.Offset) 
            : lastWrite;

        await using Stream target = await entry.OpenAsync(cancellationToken);
        await source.CopyToAsync(target, cancellationToken);
    }

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
