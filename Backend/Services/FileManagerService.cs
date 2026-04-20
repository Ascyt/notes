using Backend.Models;
using System.Text;

namespace Backend.Services;

/// <summary>
/// Service for managing files and folders within a predetermined root folder.
/// </summary>
public class FileManagerService : IFileManagerService
{
    private readonly string _rootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileManagerService"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    public FileManagerService(IConfiguration configuration)
    {
        string? configuredPath = configuration["FileManager:RootPath"];
        
        if (string.IsNullOrEmpty(configuredPath))
        {
            // Default to a "Files" folder in the application directory
            _rootPath = Path.Combine(Directory.GetCurrentDirectory(), "Files");
        }
        else
        {
            _rootPath = configuredPath;
        }

        // Ensure root directory exists
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
    }

    /// <summary>
    /// Gets the full path for a relative path, ensuring it's within the root directory.
    /// </summary>
    /// <param name="relativePath">Relative path.</param>
    /// <returns>Full resolved path.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when path is outside root directory.</exception>
    private string GetFullPath(string relativePath)
    {
        // Normalize the relative path
        string normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        
        // Combine with root path
        string fullPath = Path.Combine(_rootPath, normalizedPath);
        
        // Get the absolute path and ensure it's within the root directory
        string absolutePath = Path.GetFullPath(fullPath);
        string absoluteRoot = Path.GetFullPath(_rootPath);
        
        if (!absolutePath.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access to path outside root directory is not allowed.");
        }
        
        return absolutePath;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<FileItemDto>> ListItemsAsync(string path)
    {
        string fullPath = GetFullPath(path);
        
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        List<FileItemDto> items = new List<FileItemDto>();

        // Add directories
        foreach (string dir in Directory.GetDirectories(fullPath))
        {
            DirectoryInfo dirInfo = new DirectoryInfo(dir);
            string relativePath = Path.GetRelativePath(_rootPath, dir).Replace('\\', '/');
            
            items.Add(new FileItemDto
            {
                Name = dirInfo.Name,
                Path = relativePath,
                IsDirectory = true,
                Size = null,
                LastModified = dirInfo.LastWriteTime
            });
        }

        // Add files
        foreach (string file in Directory.GetFiles(fullPath))
        {
            FileInfo fileInfo = new FileInfo(file);
            string relativePath = Path.GetRelativePath(_rootPath, file).Replace('\\', '/');
            
            items.Add(new FileItemDto
            {
                Name = fileInfo.Name,
                Path = relativePath,
                IsDirectory = false,
                Size = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime
            });
        }

        return await Task.FromResult(items.OrderBy(i => !i.IsDirectory).ThenBy(i => i.Name));
    }

    /// <inheritdoc/>
    public async Task<string> ReadFileAsync(string path)
    {
        string fullPath = GetFullPath(path);
        
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }

        return await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
    }

    /// <inheritdoc/>
    public async Task WriteFileAsync(string path, string content)
    {
        string fullPath = GetFullPath(path);
        
        // Ensure parent directory exists
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
    }

    /// <inheritdoc/>
    public Task DeleteFileAsync(string path)
    {
        string fullPath = GetFullPath(path);
        
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }

        File.Delete(fullPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CreateFolderAsync(string path)
    {
        string fullPath = GetFullPath(path);
        
        if (Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"Folder already exists: {path}");
        }

        Directory.CreateDirectory(fullPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteFolderAsync(string path)
    {
        string fullPath = GetFullPath(path);
        
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        Directory.Delete(fullPath, recursive: true);
        return Task.CompletedTask;
    }
}
