using System.Security.Cryptography;
using Notes.Features.Components.Archive;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;

namespace Notes.Features.ZippedFolders;

/// <summary>An archive that can't be read: damaged, not what its name says, or (with a password) the password is wrong.</summary>
public sealed class ArchiveUnreadableException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>An entry of an archive, as it is read one after the other.</summary>
/// <param name="Path">Its path in the archive, with "/" between its parts and none at the end.</param>
/// <param name="LastWriteTimeUtc">When the file was last changed, where the archive says so.</param>
internal sealed record ArchiveEntry(string Path, bool IsDirectory, DateTime? LastWriteTimeUtc);

/// <summary>Reads 7z and zip archives (with SharpCompress) from the first entry to the last, as a 7z can only be read quickly that way.</summary>
internal static class ArchiveReading
{
    /// <summary>How many entries the archive has (from its index, without unpacking anything).</summary>
    public static int CountEntries(string archivePath, ArchiveFormat format, string? password)
        => Read(archivePath, format, password, archive => archive.Entries.Count());

    /// <summary>
    /// Calls <paramref name="each"/> with every entry, and the stream of its contents for a file (null for a directory), which is only good
    /// until the call returns.
    /// </summary>
    public static void ForEachEntry(string archivePath, ArchiveFormat format, string? password, Action<ArchiveEntry, Stream?> each,
        CancellationToken cancellationToken)
        => Read(archivePath, format, password, archive =>
        {
            // A 7z is read in one go (its files are packed together: reading each on its own would unpack from the start every time); a zip
            // one entry after the other, by its index
            if (format == ArchiveFormat.SevenZip)
            {
                using IReader reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Hand(reader.Entry, reader.OpenEntryStream);
                }
            }
            else
                foreach (IArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Hand(entry, entry.OpenEntryStream);
                }
            return 0;

            void Hand(SharpCompress.Common.IEntry entry, Func<Stream> open)
            {
                // Archives use "/" between the parts of a path; some made on Windows use "\", which can't be in a name there, but can on Linux
                string key = entry.Key ?? "";
                string path = (OperatingSystem.IsWindows() ? key.Replace('\\', '/') : key).Trim('/');
                // (A 7z keeps times as UTC, a zip as the local time of whoever wrote it)
                DateTime? time = entry.LastModifiedTime is { } modified
                    ? modified.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(modified, format == ArchiveFormat.SevenZip ? DateTimeKind.Utc : DateTimeKind.Local).ToUniversalTime()
                        : modified.ToUniversalTime()
                    : null;
                ArchiveEntry read = new(path, entry.IsDirectory, time);
                if (entry.IsDirectory)
                {
                    each(read, null);
                    return;
                }
                using Stream contents = open();
                each(read, contents);
            }
        });

    /// <summary>What the archive holds, with the hash of every file.</summary>
    public static FolderManifest ManifestOf(string archivePath, ArchiveFormat format, string? password, CancellationToken cancellationToken)
    {
        FolderManifest manifest = new();
        ForEachEntry(archivePath, format, password, (entry, contents) =>
        {
            if (contents is null)
                manifest.AddDirectory(entry.Path);
            else if (!manifest.AddFile(entry.Path, SHA256.HashData(contents)))
                throw new ArchiveUnreadableException($"{archivePath} has {entry.Path} twice.");
        }, cancellationToken);
        return manifest;
    }

    /// <summary>
    /// Opens the archive for <paramref name="use"/>. What goes wrong in reading it, but for the file itself (which is an
    /// <see cref="IOException"/> already) and cancelling, is an <see cref="ArchiveUnreadableException"/>: the library throws its own kinds,
    /// and a wrong password is only noticed as data that makes no sense.
    /// </summary>
    private static T Read<T>(string archivePath, ArchiveFormat format, string? password, Func<IArchive, T> use)
    {
        try
        {
            using FileStream file = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
            ReaderOptions options = new() { Password = password, LeaveStreamOpen = true };
            using IArchive archive = format == ArchiveFormat.SevenZip
                ? SevenZipArchive.OpenArchive(file, options)
                : ZipArchive.OpenArchive(file, options);
            return use(archive);
        }
        catch (Exception e) when (e is not (IOException or UnauthorizedAccessException or OperationCanceledException))
        {
            throw new ArchiveUnreadableException($"{archivePath} can't be read: {e.Message}", e);
        }
        catch (IOException e) when (e is not (ArchiveUnreadableException or FileNotFoundException or DirectoryNotFoundException)
                                    && IsAboutTheData(e))
        {
            throw new ArchiveUnreadableException($"{archivePath} can't be read: {e.Message}", e);
        }
    }

    /// <summary>
    /// Whether an <see cref="IOException"/> is about what was read rather than about reading it (SharpCompress throws some about the data:
    /// an <see cref="EndOfStreamException"/> where an archive is cut off, among others of its own).
    /// </summary>
    private static bool IsAboutTheData(IOException e)
        => e is EndOfStreamException || e.GetType().Namespace?.StartsWith("SharpCompress", StringComparison.Ordinal) == true;
}
