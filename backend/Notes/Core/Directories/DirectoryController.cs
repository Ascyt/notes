namespace Notes.Core.Directories;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class DirectoryController(Options options) : ControllerBase
{
    private readonly Options _options = options;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)] 
    public string Get()
    {
        return $"Hello from DirectoryController! Directory={_options.Directory}";
    }
}