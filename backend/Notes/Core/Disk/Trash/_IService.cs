namespace Notes.Core.Disk.Trash;

public interface IService
{
    /// <summary>
    /// Deletes the directory at <paramref name="fullPath"/>.
    /// Returns true if it was moved to recycle bin (when supported), false if permanently deleted.
    /// </summary>
    bool DeleteDirectory(string fullPath);
}
