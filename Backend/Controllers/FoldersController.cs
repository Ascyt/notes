using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Controller for folder management operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FoldersController : ControllerBase
{
    private readonly IFileManagerService _fileManagerService;
    private readonly ILogger<FoldersController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoldersController"/> class.
    /// </summary>
    /// <param name="fileManagerService">File manager service.</param>
    /// <param name="logger">Logger instance.</param>
    public FoldersController(IFileManagerService fileManagerService, ILogger<FoldersController> logger)
    {
        _fileManagerService = fileManagerService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new folder.
    /// </summary>
    /// <param name="request">Create folder request.</param>
    /// <returns>Success response.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest request)
    {
        try
        {
            await _fileManagerService.CreateFolderAsync(request.Path);
            return Ok(new { message = "Folder created successfully." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Folder already exists: {Path}", request.Path);
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt: {Path}", request.Path);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating folder: {Path}", request.Path);
            return StatusCode(500, new { message = "An error occurred while creating the folder." });
        }
    }

    /// <summary>
    /// Deletes a folder.
    /// </summary>
    /// <param name="path">Relative path to the folder.</param>
    /// <returns>Success response.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFolder([FromQuery] string path)
    {
        try
        {
            await _fileManagerService.DeleteFolderAsync(path);
            return Ok(new { message = "Folder deleted successfully." });
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
            _logger.LogError(ex, "Error deleting folder: {Path}", path);
            return StatusCode(500, new { message = "An error occurred while deleting the folder." });
        }
    }
}
