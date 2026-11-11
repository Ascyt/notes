using System.IO.Compression;
using notes.Features.Helpers;

namespace notes.Features.Components.Download;

/// <summary>Something that goes into a zip: a file, or a folder with what is in it. Where it comes from (disk, an earlier state) is up to whoever makes it.</summary>
public abstract record ZipEntry(string Name);

/// <param name="OpenRead">Opens the contents, when the zip gets to the file.</param>
public sealed record ZipFileEntry(string Name, DateTimeOffset LastWriteTime, Func<Stream> OpenRead) : ZipEntry(Name);

/// <param name="ListContents">What is in the folder, asked for when the zip gets to it (so a check made there is made for each entry as late as possible).</param>
public sealed record ZipFolderEntry(string Name, Func<IEnumerable<ZipEntry>> ListContents) : ZipEntry(Name);

public interface IZipService
{
    /// <summary>
    /// Writes what is in the folder as a zip (smallest size) to <paramref name="output"/>. Asynchronous all the way, so a slow client holds no
    /// thread while the zip waits for it.
    /// </summary>
    Task WriteZipAsync(ZipFolderEntry folder, Stream output, CancellationToken cancellationToken);
}

public sealed class ZipService : IZipService
{
    public async Task WriteZipAsync(ZipFolderEntry folder, Stream output, CancellationToken cancellationToken)
    {
        DeferredWriteStream target = new(output);
        await using (ZipArchive zip = await ZipArchive.CreateAsync(target, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
            await AddFolderAsync(zip, folder, "", cancellationToken);

        // The zip's last bytes (closing the final entry, the central directory) may have been written synchronously
        await target.FlushDeferredAsync(cancellationToken);
    }

    /// <summary>
    /// A directory on disk, for <see cref="WriteZipAsync"/>. Only entries that <paramref name="include"/> agrees to go into the zip (and only
    /// directories it agrees to are looked into), each asked for when the zip gets to it. Links and the folder of the app's own data never do.
    /// </summary>
    public static ZipFolderEntry FolderOnDisk(string physicalDirectory, Func<FileSystemInfo, bool> include)
        => new(Path.GetFileName(physicalDirectory), () => new DirectoryInfo(physicalDirectory).EnumerateFileSystemInfos()
            // Same rules as the listing and the path service: nothing that is a link (it could point out of the notes), no reserved folder
            .Where(entry => !PathService.IsLink(entry) && !(entry is DirectoryInfo && entry.Name == PathService.ReservedName) && include(entry))
            .Select(entry => entry is DirectoryInfo directory
                ? FolderOnDisk(directory.FullName, include)
                : (ZipEntry)new ZipFileEntry(entry.Name, entry.LastWriteTime,
                    // A note being edited at the moment is still read
                    () => new FileStream(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true))));

    /// <param name="prefix">Zip path of the folder: "" for the top level, otherwise ending in "/".</param>
    private static async Task AddFolderAsync(ZipArchive zip, ZipFolderEntry folder, string prefix, CancellationToken cancellationToken)
    {
        ZipEntry[] entries = [.. folder.ListContents()];

        // Keeps empty folders, which only exist in a zip as an entry of their own
        if (entries.Length == 0 && prefix != "")
            _ = zip.CreateEntry(prefix);

        foreach (ZipEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry is ZipFolderEntry subfolder)
                await AddFolderAsync(zip, subfolder, prefix + subfolder.Name + "/", cancellationToken);
            else
                await AddFileAsync(zip, (ZipFileEntry)entry, prefix + entry.Name, cancellationToken);
        }
    }

    private static async Task AddFileAsync(ZipArchive zip, ZipFileEntry file, string entryName, CancellationToken cancellationToken)
    {
        await using Stream source = file.OpenRead();

        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);
        // Zip can only store times from 1980 to 2107
        entry.LastWriteTime = file.LastWriteTime.Year is < 1980 or > 2107
            ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, file.LastWriteTime.Offset)
            : file.LastWriteTime;

        await using Stream target = await entry.OpenAsync(cancellationToken);
        await source.CopyToAsync(target, cancellationToken);
    }
}
