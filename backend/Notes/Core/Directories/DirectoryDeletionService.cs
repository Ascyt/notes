using Microsoft.VisualBasic.FileIO;

namespace Notes.Core.Directories;

public sealed class DirectoryDeletionService : IDirectoryDeletionService
{
    public bool DeleteDirectory(string fullPath)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return true;
            }
            catch
            {
                // Fall back to permanent delete below.
            }
        }

        Directory.Delete(fullPath, recursive: true);
        return false;
    }
}
