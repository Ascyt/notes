using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace notes.Features.View.Editor;

/// <summary>Answers the editor's save: a POST whose body is the new contents of the file.</summary>
public sealed class SaveHandler
{
    public async Task<IActionResult> HandleAsync(string physicalPath, Stream body)
    {
        if (!File.Exists(physicalPath))
            return new NotFoundObjectResult("File not found.");

        using StreamReader reader = new(body, Encoding.UTF8);
        string contents = await reader.ReadToEndAsync();
        await File.WriteAllTextAsync(physicalPath, contents);

        return new OkResult();
    }
}
