namespace Backend.Models;

/// <summary>
/// Request model for creating or updating a file.
/// </summary>
public class FileContentRequest
{
    /// <summary>
    /// Gets or sets the relative path of the file.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Gets or sets the content of the file.
    /// </summary>
    public required string Content { get; set; }
}
