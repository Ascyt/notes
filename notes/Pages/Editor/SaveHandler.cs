using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;

namespace notes.Features.View.Editor;

/// <summary>Answers the editor's save: a POST whose body is the new contents of the file. Needs write access to the file.</summary>
public sealed class SaveHandler(AccessTree tree)
{
    public async Task<IActionResult> HandleAsync(ClaimsPrincipal user, string physicalPath, Stream body)
    {
        if (!File.Exists(physicalPath))
            return new NotFoundObjectResult("File not found.");
        if (!tree.GetAccess(user, physicalPath).CanWrite)
            return new ObjectResult("You may not change this file.") { StatusCode = StatusCodes.Status403Forbidden };

        using StreamReader reader = new(body, Encoding.UTF8);
        string contents = await reader.ReadToEndAsync();
        await File.WriteAllTextAsync(physicalPath, contents);

        return new OkResult();
    }
}
