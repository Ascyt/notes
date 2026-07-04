namespace Notes.Core.Disk.Directory;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/disk/dir")]
public sealed class Controller(Options options, IService service, Trash.IService trashService, Naming.IService namingService) : ControllerBase
{
    private readonly Options _options = options;
    private readonly IService _service = service;
    private readonly Trash.IService _trashService = trashService;
    private readonly Naming.IService _namingService = namingService;

    [HttpGet("{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)] 
    public async Task<IActionResult> GetAsync(string? path)
    {
        if (!TryResolvePath(path, out string virtualPath, out string fullPath, out IActionResult? errorResult))
        {
            return errorResult!;
        }

        if (System.IO.Directory.Exists(fullPath))
        {
            return Ok(new
            {
                VirtualPath = virtualPath,
                PhysicalPath = System.IO.Path.GetFullPath(fullPath),
                Model = await _service.LoadModelAsync(fullPath)
            });
        }
        
        if (System.IO.File.Exists(fullPath))
            return UnprocessableEntity($"The path points to a file, not a directory: `{fullPath}`");
        
        return NotFound($"Not found: `{fullPath}`");  
    }

    [HttpPost("{*path}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(string? path)
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

        if (!System.IO.Directory.Exists(fullPath))
        {
            return NotFound($"Directory does not exist: `{fullPath}`");
        }

        string newDirName = _namingService.GetFormattedName(fullPath, null);

        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(fullPath, newDirName));

        await _service.SaveModelAsync(fullPath, new Model
        {
            Name = "Untitled",
            Files = []
        });

        return CreatedAtAction(nameof(GetAsync), new { path = virtualPath.TrimStart('/') }, new
        {
            VirtualPath = virtualPath,
            PhysicalPath = System.IO.Path.GetFullPath(fullPath),
        });
    }

    [HttpDelete("{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAsync(string? path)
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

        if (!System.IO.Directory.Exists(fullPath))
        {
            return NotFound($"Not found: `{fullPath}`");
        }

        bool movedToRecycleBin = _trashService.DeleteDirectory(fullPath);

        return Ok(new
        {
            VirtualPath = virtualPath,
            PhysicalPath = System.IO.Path.GetFullPath(fullPath),
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