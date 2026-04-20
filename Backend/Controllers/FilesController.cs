using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Controller for file management operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileManagerService _fileManagerService;
    private readonly ILogger<FilesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilesController"/> class.
    /// </summary>
    /// <param name="fileManagerService">File manager service.</param>
    /// <param name="logger">Logger instance.</param>
    public FilesController(IFileManagerService fileManagerService, ILogger<FilesController> logger)
    {
        _fileManagerService = fileManagerService;
        _logger = logger;
    }

    /// <summary>
    /// Lists files and folders in the specified path.
    /// </summary>
    /// <param name="path">Relative path (default is root).</param>
    /// <returns>List of file and folder items.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FileItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListItems([FromQuery] string path = "")
    {
        try
        {
            IEnumerable<FileItemDto> items = await _fileManagerService.ListItemsAsync(path);
            return Ok(items);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Directory not found: {Path}", path);
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt: {Path}", path);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing items at path: {Path}", path);
            return StatusCode(500, new { message = "An error occurred while listing items." });
        }
    }

    /// <summary>
    /// Reads the content of a file.
    /// </summary>
    /// <param name="path">Relative path to the file.</param>
    /// <returns>File content.</returns>
    [HttpGet("content")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReadFile([FromQuery] string path)
    {
        try
        {
            string content = await _fileManagerService.ReadFileAsync(path);
            return Ok(new { content });
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "File not found: {Path}", path);
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt: {Path}", path);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {Path}", path);
            return StatusCode(500, new { message = "An error occurred while reading the file." });
        }
    }

    /// <summary>
    /// Creates or updates a file.
    /// </summary>
    /// <param name="request">File content request.</param>
    /// <returns>Success response.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> WriteFile([FromBody] FileContentRequest request)
    {
        try
        {
            await _fileManagerService.WriteFileAsync(request.Path, request.Content);
            return Ok(new { message = "File saved successfully." });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt: {Path}", request.Path);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing file: {Path}", request.Path);
            return StatusCode(500, new { message = "An error occurred while writing the file." });
        }
    }

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">Relative path to the file.</param>
    /// <returns>Success response.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFile([FromQuery] string path)
    {
        try
        {
            await _fileManagerService.DeleteFileAsync(path);
            return Ok(new { message = "File deleted successfully." });
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "File not found: {Path}", path);
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt: {Path}", path);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file: {Path}", path);
            return StatusCode(500, new { message = "An error occurred while deleting the file." });
        }
    }
}
