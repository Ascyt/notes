using Microsoft.AspNetCore.Mvc;
using notes.Features.Helpers;

namespace notes.Features.Components.Rename;

/// <summary>Answers <c>?rename=&lt;name&gt;</c>: renames the file or directory and returns where it is now.</summary>
public sealed class RenameHandler(Config config, IPathService path)
{
    public IActionResult Handle(string virtualPath, string physicalPath, string newName)
    {
        if (virtualPath == "")
            return new BadRequestObjectResult("Cannot rename the root directory.");
        if (!path.IsValidName(newName))
            return new BadRequestObjectResult("Invalid name.");

        bool isDirectory = Directory.Exists(physicalPath);
        if (!isDirectory && !File.Exists(physicalPath))
            return new NotFoundObjectResult("Path not found.");

        string newPhysicalPath = Path.Combine(Path.GetDirectoryName(physicalPath)!, newName);
        // Differing only in case is the same entry, so that is allowed
        if (Path.Exists(newPhysicalPath) && !string.Equals(newPhysicalPath, physicalPath, StringComparison.OrdinalIgnoreCase))
            return new ConflictObjectResult("A file or directory with that name already exists.");

        if (isDirectory)
            Directory.Move(physicalPath, newPhysicalPath);
        else
            File.Move(physicalPath, newPhysicalPath);

        return new ContentResult { Content = "/" + path.ToVirtualPath(config.Dir, newPhysicalPath) };
    }
}
