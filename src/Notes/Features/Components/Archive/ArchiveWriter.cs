using ICSharpCode.SharpZipLib.Zip;
using Notes.Features.Components.Download;
using SharpCompress.Common;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Writers.SevenZip;
using SharpZipEntry = ICSharpCode.SharpZipLib.Zip.ZipEntry;
using ZipEntry = Notes.Features.Components.Download.ZipEntry;

namespace Notes.Features.Components.Archive;

/// <summary>
/// Writes what is in a folder into an archive file of the chosen format (<see cref="ArchiveOptions"/>): 7z with SharpCompress, zip with
/// SharpZipLib (which, unlike the zip of .NET, can encrypt). A 7z can only be written to a file, not sent while it is made, as its header is
/// filled in at the end.
/// </summary>
public static class ArchiveWriter
{
    /// <summary>
    /// Writes the archive to a new file at <paramref name="targetPath"/>. Each entry of the folder is listed and opened when the writer gets to
    /// it (see <see cref="ZipFolderEntry.ListContents"/>).
    /// </summary>
    /// <param name="progress">Counts what is read, to tell how far it got; none if nobody waits to see that.</param>
    public static async Task WriteAsync(ZipFolderEntry folder, ArchiveOptions options, string targetPath,
        CancellationToken cancellationToken, ArchiveProgress? progress = null)
    {
        // ReadWrite: the 7z writer goes back to the start to fill in where its header is. (Others may read it meanwhile: the tests of
        // zipped folders copy what is on disk while it is written.)
        await using FileStream file = new(targetPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 81920, useAsync: true);
        Func<ZipFileEntry, Stream> open = progress is null ? entry => entry.OpenRead() : entry => progress.Counting(entry.OpenRead());
        if (options.Format == ArchiveFormat.SevenZip)
            await WriteSevenZipAsync(folder, options, file, open, cancellationToken);
        else
            await WriteZipAsync(folder, options, file, open, cancellationToken);
    }

    // ---- 7z

    /// <summary>
    /// The dictionary size and the number of "fast bytes" of LZMA2 for each level (the writer's own level setting changes nothing). The
    /// encoder takes about eleven times the dictionary in memory while it runs, so Max stays well below what 7-Zip's "Ultra" takes.
    /// </summary>
    private static LzmaEncoderProperties LzmaPropertiesFor(ArchiveCompression compression) => compression switch
    {
        ArchiveCompression.Min => new LzmaEncoderProperties(false, 64 << 10, 8),
        ArchiveCompression.Medium => new LzmaEncoderProperties(false, 1 << 20, 32),
        ArchiveCompression.High => new LzmaEncoderProperties(false, 8 << 20, 64),
        ArchiveCompression.Max => new LzmaEncoderProperties(false, 32 << 20, 128),
        _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, "The 7z writer always compresses.")
    };

    private static async Task WriteSevenZipAsync(ZipFolderEntry folder, ArchiveOptions options, Stream output, Func<ZipFileEntry, Stream> open,
        CancellationToken cancellationToken)
    {
        SevenZipWriterOptions writerOptions = new(CompressionType.LZMA2)
        {
            LzmaProperties = LzmaPropertiesFor(options.Compression),
            Password = options.Password,
            // With a password, the names inside are encrypted too, so nothing about the contents can be read without it
            EncryptHeader = options.Password is not null,
            LeaveStreamOpen = true
        };
        await using SevenZipWriter writer = new(output, writerOptions);
        await AddToSevenZipAsync(writer, folder, "", open, cancellationToken);
    }

    /// <param name="prefix">Path of the folder in the archive: "" for the top level, otherwise ending in "/".</param>
    private static async Task AddToSevenZipAsync(SevenZipWriter writer, ZipFolderEntry folder, string prefix, Func<ZipFileEntry, Stream> open,
        CancellationToken cancellationToken)
    {
        foreach (ZipEntry entry in folder.ListContents())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = prefix + entry.Name;
            if (entry is ZipFolderEntry subfolder)
            {
                // Every folder is an entry of its own, so empty ones are kept
                await writer.WriteDirectoryAsync(name, null, cancellationToken);
                await AddToSevenZipAsync(writer, subfolder, name + "/", open, cancellationToken);
                continue;
            }

            ZipFileEntry fileEntry = (ZipFileEntry)entry;
            await using (Stream source = open(fileEntry))
                await writer.WriteAsync(name, source, fileEntry.LastWriteTime.UtcDateTime, cancellationToken);
        }
    }

    // ---- zip

    /// <summary>The deflate level for each level (none for <see cref="ArchiveCompression.Store"/>, whose files are stored as they are).</summary>
    private static int DeflateLevelFor(ArchiveCompression compression) => compression switch
    {
        ArchiveCompression.Store => 0,
        ArchiveCompression.Medium => 5,
        ArchiveCompression.High => 7,
        _ => 9
    };

    private static async Task WriteZipAsync(ZipFolderEntry folder, ArchiveOptions options, Stream output, Func<ZipFileEntry, Stream> open,
        CancellationToken cancellationToken)
    {
        await using ZipOutputStream zip = new(output) { IsStreamOwner = false };
        zip.SetLevel(DeflateLevelFor(options.Compression));
        if (options.Password is not null)
            zip.Password = options.Password;

        await AddToZipAsync(zip, folder, "", options, open, cancellationToken);
        await zip.FinishAsync(cancellationToken);
    }

    private static async Task AddToZipAsync(ZipOutputStream zip, ZipFolderEntry folder, string prefix, ArchiveOptions options,
        Func<ZipFileEntry, Stream> open, CancellationToken cancellationToken)
    {
        ZipEntry[] entries = [.. folder.ListContents()];

        // Keeps empty folders, which only exist in a zip as an entry of their own
        if (entries.Length == 0 && prefix != "")
        {
            await zip.PutNextEntryAsync(new SharpZipEntry(prefix) { IsUnicodeText = true }, cancellationToken);
            await zip.CloseEntryAsync(cancellationToken);
        }

        foreach (ZipEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is ZipFolderEntry subfolder)
            {
                await AddToZipAsync(zip, subfolder, prefix + subfolder.Name + "/", options, open, cancellationToken);
                continue;
            }

            ZipFileEntry fileEntry = (ZipFileEntry)entry;
            SharpZipEntry zipEntry = new(prefix + entry.Name)
            {
                IsUnicodeText = true,
                // Zip can only store times from 1980 to 2107
                DateTime = fileEntry.LastWriteTime.Year is < 1980 or > 2107 ? new DateTime(1980, 1, 1) : fileEntry.LastWriteTime.LocalDateTime,
                // AES, which 7-Zip and the other programs open, rather than zip's old encryption, which is easily broken
                AESKeySize = options.Password is null ? 0 : 256
            };
            if (options.Compression == ArchiveCompression.Store)
                zipEntry.CompressionMethod = CompressionMethod.Stored;

            await using (Stream source = open(fileEntry))
            {
                await zip.PutNextEntryAsync(zipEntry, cancellationToken);
                await source.CopyToAsync(zip, cancellationToken);
                await zip.CloseEntryAsync(cancellationToken);
            }
        }
    }
}
