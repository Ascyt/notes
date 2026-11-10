using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.History;

namespace notes.Pages.Editor;

/// <summary>Answers the editor's save: a POST whose body is the new contents of the file. Needs write access to the file.</summary>
public sealed class SaveHandler(AccessTree tree, HistoryService history)
{
    public async Task<IActionResult> HandleAsync(ClaimsPrincipal user, string physicalPath, Stream body)
    {
        if (!File.Exists(physicalPath))
            return new NotFoundObjectResult("File not found.");
        if (!tree.GetAccess(user, physicalPath).CanWrite)
            return new ObjectResult("Insufficient permissions to change this file.") { StatusCode = StatusCodes.Status403Forbidden };

        using StreamReader reader = new(body, Encoding.UTF8);
        string contents = await reader.ReadToEndAsync();
        history.BeforeChange(user, [physicalPath], contents);
        await File.WriteAllTextAsync(physicalPath, contents);
        history.AfterChange(user, [physicalPath], HistoryChangeKind.Save);

        return new OkResult();
    }
}
