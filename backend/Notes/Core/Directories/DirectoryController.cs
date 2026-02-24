namespace Notes.Core.Directories;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class DirectoryController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)] 
    public string Get()
    {
        return "Hello from DirectoryController!";
    }
}