using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;

namespace notes.Features.Components.Download;

/// <summary>
/// Answers <c>?download</c>: a file as it is, a directory as a zip. The zip leaves out the entries the user may not read (with what is in
/// them), like a copy does. The caller has made sure the user may open the entry itself.
/// </summary>
public sealed class DownloadHandler(Config config, IZipService zipService, AccessTree tree)
{
    public IActionResult Handle(ClaimsPrincipal user, string virtualPath, string physicalPath)
    {
        if (File.Exists(physicalPath))
            return new PhysicalFileResult(physicalPath, "application/octet-stream")
            {
                FileDownloadName = Path.GetFileName(physicalPath),
                EnableRangeProcessing = true
            };

        // Inside a directory the user may open, they may open whatever they may read, and the zip doesn't go into anything else
        ZipFolderEntry folder = ZipService.FolderOnDisk(physicalPath, entry => tree.GetAccess(user, entry.FullName).CanRead);
        return ZipDownload(folder, virtualPath);
    }

    /// <summary>The folder as a zip download, named after the directory at <paramref name="virtualPath"/> (after the site for the root).</summary>
    public IActionResult ZipDownload(ZipFolderEntry folder, string virtualPath)
    {
        // The root has no name of its own
        string directoryName = virtualPath == "" ? config.Title : folder.Name;
        directoryName = Path.GetInvalidFileNameChars().Aggregate(directoryName, (current, invalid) => current.Replace(invalid, '_'));
        return new ZipDirectoryResult(zipService, folder, directoryName + ".zip");
    }
}
