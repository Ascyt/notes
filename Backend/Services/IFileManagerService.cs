using Backend.Models;

namespace Backend.Services;

/// <summary>
/// Interface for file management operations.
/// </summary>
public interface IFileManagerService
{
    /// <summary>
    /// Lists files and folders in the specified path.
    /// </summary>
    /// <param name="path">Relative path to list contents of.</param>
    /// <returns>List of file and folder items.</returns>
    Task<IEnumerable<FileItemDto>> ListItemsAsync(string path);

    /// <summary>
    /// Reads the content of a file.
    /// </summary>
    /// <param name="path">Relative path to the file.</param>
    /// <returns>File content as string.</returns>
    Task<string> ReadFileAsync(string path);

    /// <summary>
    /// Creates or updates a file with the specified content.
    /// </summary>
    /// <param name="path">Relative path to the file.</param>
    /// <param name="content">Content to write to the file.</param>
    Task WriteFileAsync(string path, string content);

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">Relative path to the file.</param>
    Task DeleteFileAsync(string path);

    /// <summary>
    /// Creates a new folder.
    /// </summary>
    /// <param name="path">Relative path to the folder.</param>
    Task CreateFolderAsync(string path);

    /// <summary>
    /// Deletes a folder.
    /// </summary>
    /// <param name="path">Relative path to the folder.</param>
    Task DeleteFolderAsync(string path);
}
