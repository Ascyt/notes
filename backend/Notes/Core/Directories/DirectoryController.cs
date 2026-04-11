namespace Notes.Core.Directories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;

[ApiController]
[Route("api/dir")]
public class DirectoryController(Options options) : ControllerBase
{
    private readonly Options _options = options;

    [HttpGet("{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)] 
    public IActionResult Get(string? path)
    {
        if (!TryResolvePath(path, out string virtualPath, out string fullPath, out IActionResult? errorResult))
        {
            return errorResult!;
        }

        if (Directory.Exists(fullPath))
        {
            List<object> items = [..Directory.EnumerateFileSystemEntries(fullPath)
                .Select(entry =>
                {
                    FileAttributes attributes = System.IO.File.GetAttributes(entry);
                    return new
                    {
                        Name = Path.GetFileName(entry),
                        SizeBytes = attributes.HasFlag(FileAttributes.Directory) ? 0 : new FileInfo(entry).Length,
                        LastModified = new FileInfo(entry).LastWriteTime,
                        IsDirectory = attributes.HasFlag(FileAttributes.Directory)
                    };
                })];

            return Ok(new
            {
                VirtualPath = virtualPath,
                PhysicalPath = Path.GetFullPath(fullPath),
                Items = items
            });
        }
        
        if (System.IO.File.Exists(fullPath))
            return UnprocessableEntity($"The path points to a file, not a directory: `{fullPath}`");
        
        return NotFound($"Not found: `{fullPath}`");  
    }

    [HttpPost("{*path}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public IActionResult Create(string? path)
    {
        if (!TryResolvePath(path, out string virtualPath, out string fullPath, out IActionResult? errorResult))
        {
            return errorResult!;
        }

        if (virtualPath == "/")
        {
            return BadRequest("Refusing to create the root directory.");
        }

        if (System.IO.File.Exists(fullPath))
        {
            return UnprocessableEntity($"The path points to a file, not a directory: `{fullPath}`");
        }

        if (Directory.Exists(fullPath))
        {
            return Conflict($"Directory already exists: `{fullPath}`");
        }

        Directory.CreateDirectory(fullPath);

        return CreatedAtAction(nameof(Get), new { path = virtualPath.TrimStart('/') }, new
        {
            VirtualPath = virtualPath,
            PhysicalPath = Path.GetFullPath(fullPath),
        });
    }

    [HttpDelete("{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Delete(string? path)
    {
        if (!TryResolvePath(path, out string virtualPath, out string fullPath, out IActionResult? errorResult))
        {
            return errorResult!;
        }

        if (virtualPath == "/")
        {
            return BadRequest("Refusing to delete the root directory.");
        }

        if (System.IO.File.Exists(fullPath))
        {
            return UnprocessableEntity($"The path points to a file, not a directory: `{fullPath}`");
        }

        if (!Directory.Exists(fullPath))
        {
            return NotFound($"Not found: `{fullPath}`");
        }

        bool movedToRecycleBin = false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                movedToRecycleBin = true;
            }
            catch
            {
                // Fall back to permanent delete below.
            }
        }

        if (!movedToRecycleBin)
        {
            Directory.Delete(fullPath, recursive: true);
        }

        return Ok(new
        {
            VirtualPath = virtualPath,
            PhysicalPath = Path.GetFullPath(fullPath),
            Recycled = movedToRecycleBin
        });
    }

    private bool TryResolvePath(string? rawPath, out string virtualPath, out string fullPath, out IActionResult? errorResult)
    {
        if (string.IsNullOrWhiteSpace(_options.Directory))
        {
            virtualPath = "";
            fullPath = "";
            errorResult = StatusCode(StatusCodes.Status500InternalServerError, "Server misconfiguration: no root directory configured.");
            return false;
        }

        string decoded = rawPath == null ? "/" : Uri.UnescapeDataString(rawPath);
        if (!decoded.StartsWith('/'))
        {
            virtualPath = "";
            fullPath = "";
            errorResult = BadRequest($"Invalid path (provided: {decoded}). A valid path must start with `/`.");
            return false;
        }

        string relative = decoded.TrimStart('/');
        relative = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(relative))
        {
            virtualPath = "";
            fullPath = "";
            errorResult = BadRequest("Invalid path: must be a relative path.");
            return false;
        }

        string rootFullPath = Path.GetFullPath(_options.Directory);
        string candidateFullPath = Path.GetFullPath(Path.Combine(rootFullPath, relative));

        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string rootWithSeparator = rootFullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!(candidateFullPath.Equals(rootFullPath, comparison) || candidateFullPath.StartsWith(rootWithSeparator, comparison)))
        {
            virtualPath = "";
            fullPath = "";
            errorResult = BadRequest("Invalid path: resolved path escapes the configured root directory.");
            return false;
        }

        virtualPath = "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
        if (virtualPath == "//")
        {
            virtualPath = "/";
        }
        fullPath = candidateFullPath;
        errorResult = null;
        return true;
    }
}