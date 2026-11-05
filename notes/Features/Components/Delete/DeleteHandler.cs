using Microsoft.AspNetCore.Mvc;

namespace notes.Features.Components.Delete;

/// <summary>Answers a DELETE request: removes the file, or the directory and everything in it.</summary>
public sealed class DeleteHandler
{
    public IActionResult Handle(string virtualPath, string physicalPath)
    {
        if (virtualPath == "")
            return new BadRequestObjectResult("Cannot delete the root directory.");

        if (Directory.Exists(physicalPath))
            Directory.Delete(physicalPath, recursive: true);
        else if (File.Exists(physicalPath))
            File.Delete(physicalPath);
        else
            return new NotFoundObjectResult("Path not found.");

        return new OkResult();
    }
}
