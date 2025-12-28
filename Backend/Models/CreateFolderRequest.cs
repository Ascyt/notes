namespace Backend.Models;

/// <summary>
/// Request model for creating a folder.
/// </summary>
public class CreateFolderRequest
{
    /// <summary>
    /// Gets or sets the relative path of the folder to create.
    /// </summary>
    public required string Path { get; set; }
}
