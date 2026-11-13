namespace notes.Features.Configuration;

/// <summary>
/// The configuration of the notes as it was at some point (an earlier state, see the history), and which of their paths were directories,
/// for <see cref="Auth.IEntryAccess"/> to decide by. Paths are physical, as if the notes were on disk as they were then.
/// </summary>
public interface IConfigSnapshot
{
    /// <summary>What the folder's configuration said then: nothing if there was none, otherwise what was in it, or why that couldn't be used.</summary>
    FolderConfigState FolderConfigOf(string physicalDirectory);

    /// <summary>Whether there was a directory at the path then.</summary>
    bool IsDirectory(string physicalPath);
}
