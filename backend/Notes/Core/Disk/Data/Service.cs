using Microsoft.VisualBasic.FileIO;

namespace Notes.Core.Disk.Data;

public sealed class Service : IService
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

        System.IO.Directory.Delete(fullPath, recursive: true);
        return false;
    }
}
