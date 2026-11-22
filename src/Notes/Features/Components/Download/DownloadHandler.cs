using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.EntryActions;

namespace Notes.Features.Components.Download;

/// <summary>
/// Answers <c>?download</c>: a file as it is, a directory as a zip. The zip leaves out the entries the user may not read (with what is in
/// them), like a copy does. The caller has made sure the user may open the entry itself.
/// </summary>
public sealed class DownloadHandler(Config config, EntryLocations locations, IZipService zipService, AccessTree tree) : IEntryGetAction
{
    public const string DownloadParameter = "download";

    public string Parameter => DownloadParameter;
    public AccessNeeded Needs => AccessNeeded.Readable;

    public Task<IActionResult> GetAsync(EntryRequest request)
        => Task.FromResult(Download(request.User, request.VirtualPath, request.PhysicalPath));

    private IActionResult Download(ClaimsPrincipal user, string virtualPath, string physicalPath)
    {
        if (File.Exists(physicalPath))
            return new PhysicalFileResult(physicalPath, "application/octet-stream")
            {
                FileDownloadName = Path.GetFileName(physicalPath),
                EnableRangeProcessing = true
            };

        // Inside a directory the user may open, they may open whatever they may read, and the zip doesn't go into anything else
        ZipFolderEntry folder = ZipService.FolderOnDisk(locations, physicalPath, entry =>tree.GetAccess(user, entry.FullName).CanRead);
        return ZipDownload(folder, virtualPath);
    }

    /// <summary>The folder as a zip download, named after the directory at <paramref name="virtualPath"/> (after the site for the root).</summary>
    public IActionResult ZipDownload(ZipFolderEntry folder, string virtualPath)
        => new ZipDirectoryResult(zipService, folder, DownloadNameOf(virtualPath, folder.Name) + ".zip");

    /// <summary>
    /// The name an archive of the directory at <paramref name="virtualPath"/> is downloaded as, without its extension: the directory's name, or
    /// the site's title for the root, which has no name of its own.
    /// </summary>
    public string DownloadNameOf(string virtualPath, string directoryName)
    {
        string name = virtualPath == "" ? config.Title : directoryName;
        return Path.GetInvalidFileNameChars().Aggregate(name, (current, invalid) => current.Replace(invalid, '_'));
    }
}
