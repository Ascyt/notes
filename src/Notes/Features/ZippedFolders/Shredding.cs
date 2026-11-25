using System.Security.Cryptography;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// Overwrites files with random data before they are deleted: a file that is only deleted stays on the disk, readable, until something else
/// is written where it was. For what was in an encrypted folder while it was unzipped (<see cref="ArchivesConfig.OverwritePasses"/>).
/// </summary>
/// <remarks>
/// Only as good as the disk lets it be: an SSD (wear levelling), a copy-on-write file system (btrfs, ZFS, APFS) or a snapshot may keep the old
/// blocks anyway. Encrypting the whole disk is what helps there (see docs/administration/archived-directories.md).
/// </remarks>
public static class Shredding
{
    private const int BufferSize = 1 << 20;

    /// <summary>Overwrites the file's contents (as long as it is) <paramref name="passes"/> times, each pass flushed to the disk.</summary>
    public static void Overwrite(string file, int passes)
    {
        if (passes <= 0 || !File.Exists(file))
            return;
        File.SetAttributes(file, FileAttributes.Normal);
        using FileStream stream = new(file, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize);
        long length = stream.Length;
        byte[] buffer = new byte[(int)Math.Min(BufferSize, Math.Max(1, length))];
        for (int pass = 0; pass < passes; pass++)
        {
            stream.Position = 0;
            for (long written = 0; written < length; written += buffer.Length)
            {
                int count = (int)Math.Min(buffer.Length, length - written);
                RandomNumberGenerator.Fill(buffer.AsSpan(0, count));
                stream.Write(buffer, 0, count);
            }
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>Overwrites every file in the directory (and below), as <see cref="Overwrite"/> does. Links aren't followed.</summary>
    public static void OverwriteTree(string directory, int passes)
    {
        if (passes <= 0 || !Directory.Exists(directory))
            return;
        foreach (string file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions
                 {
                     RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true
                 }))
            Overwrite(file, passes);
    }
}
