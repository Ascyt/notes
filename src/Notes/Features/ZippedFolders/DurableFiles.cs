using System.Runtime.InteropServices;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// Makes sure what was written is on the disk, not only in the system's cache, before a step relies on it: a power cut otherwise loses what
/// the cache held, also where a later step (deleting the unzipped copy) already made it to the disk.
/// </summary>
/// <remarks>
/// On Linux, a new or renamed entry is only on the disk once its directory is flushed too, which .NET has no call for: it is done with
/// <c>open</c> and <c>fsync</c> of the C library. Windows has no such step (NTFS keeps its directories in its journal), so it is skipped there.
/// </remarks>
internal static class DurableFiles
{
    /// <summary>Writes the file's contents to the disk.</summary>
    public static void FlushFile(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        file.Flush(flushToDisk: true);
    }

    /// <summary>Writes the directory's list of entries (what was created, renamed or deleted in it) to the disk.</summary>
    public static void FlushDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        int handle = Open(path, ReadOnly);
        if (handle < 0)
            throw new IOException($"Could not open {path} to flush it (error {Marshal.GetLastPInvokeError()}).");
        try
        {
            if (Fsync(handle) != 0)
                throw new IOException($"Could not flush {path} (error {Marshal.GetLastPInvokeError()}).");
        }
        finally
        {
            _ = Close(handle);
        }
    }

    /// <summary>Flushes every file in the directory, then every directory in it from the deepest up, the directory itself last.</summary>
    public static void FlushTree(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", AllEntries))
            FlushFile(file);
        foreach (string inner in Directory.EnumerateDirectories(directory, "*", AllEntries).OrderByDescending(path => path.Length))
            FlushDirectory(inner);
        FlushDirectory(directory);
    }

    private static readonly EnumerationOptions AllEntries = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };

    private const int ReadOnly = 0;

    // (DllImport rather than LibraryImport, which would need unsafe code allowed in the project; strings go as UTF-8 outside Windows)
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int handle);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int handle);
}
