namespace Notes.Core.Directories;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/dir")]
public class DirectoryController(Options options) : ControllerBase
{
    private readonly Options _options = options;

    [HttpGet("{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)] 
    public IActionResult Get(string path)
    {
        path = Uri.UnescapeDataString(path);

        if (!path.StartsWith('/'))
        {
            return BadRequest($"Invalid path (provided: {path}). A valid path must start with `/`.");
        }
        path = path.TrimStart('/');

        string fullPath = Path.Combine(_options.Directory, path);
        if (Directory.Exists(fullPath))
        {
            List<ListingItemDto> items = [..Directory.EnumerateFileSystemEntries(fullPath)
                .Select(entry =>
                {
                    FileAttributes attributes = System.IO.File.GetAttributes(entry);
                    return new ListingItemDto
                    {
                        Name = Path.GetFileName(entry),
                        SizeBytes = attributes.HasFlag(FileAttributes.Directory) ? 0 : new FileInfo(entry).Length,
                        LastModified = new FileInfo(entry).LastWriteTime,
                        IsDirectory = attributes.HasFlag(FileAttributes.Directory)
                    };
                })];

            return Ok(new DirectoryListingDto
            {
                VirtualPath = $"/{path}",
                PhysicalPath = Path.GetFullPath(fullPath),
                Items = items
            });
        }
        
        if (System.IO.File.Exists(fullPath))
            return UnprocessableEntity($"The path points to a file, not a directory: `{fullPath}`");
        
        return NotFound($"Not found: `{fullPath}`");  
    }
}