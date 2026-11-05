using Microsoft.AspNetCore.Mvc;
using notes.Features.Helpers;

namespace notes.Features.Components.Create;

/// <summary>Answers <c>?create=file</c> and <c>?create=directory</c>: makes a timestamp-named entry in the directory and redirects to it.</summary>
public sealed class CreateHandler(Config config, IPathService path)
{
    public IActionResult Handle(string physicalDirectory, string create)
    {
        if (!Directory.Exists(physicalDirectory))
            return new NotFoundObjectResult("Directory not found.");
        if (create is not ("file" or "directory"))
            return new BadRequestObjectResult("Invalid create kind.");

        bool isFile = create == "file";
        string baseName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
        string extension = isFile ? ".md" : "";

        string name = baseName + extension;
        for (int n = 1; Path.Exists(Path.Combine(physicalDirectory, name)); n++)
            name = $"{baseName}_{n}{extension}";

        string newPhysicalPath = Path.Combine(physicalDirectory, name);
        if (isFile)
            File.WriteAllText(newPhysicalPath, "");
        else
            Directory.CreateDirectory(newPhysicalPath);

        return new RedirectResult("/" + path.ToVirtualPath(config.Dir, newPhysicalPath));
    }
}
