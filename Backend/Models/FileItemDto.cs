namespace Backend.Models;

/// <summary>
/// Data transfer object representing a file or folder item.
/// </summary>
public class FileItemDto
{
    /// <summary>
    /// Gets or sets the name of the file or folder.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the relative path of the file or folder.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item is a directory.
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Gets or sets the size of the file in bytes (null for directories).
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// Gets or sets the last modified date and time.
    /// </summary>
    public DateTime LastModified { get; set; }
}
