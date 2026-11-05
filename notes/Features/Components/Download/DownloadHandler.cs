using Microsoft.AspNetCore.Mvc;

namespace notes.Features.Components.Download;

/// <summary>Answers <c>?download</c>: a file as it is, a directory as a zip.</summary>
public sealed class DownloadHandler(Config config, IZipService zipService)
{
    public IActionResult Handle(string virtualPath, string physicalPath, bool isAdmin)
    {
        // The policy lets anyone view the root page, so this must not rely on it
        if (!isAdmin)
            return new NotFoundResult();

        if (File.Exists(physicalPath))
            return new PhysicalFileResult(physicalPath, "application/octet-stream")
            {
                FileDownloadName = Path.GetFileName(physicalPath),
                EnableRangeProcessing = true
            };

        // The root has no name of its own
        string directoryName = virtualPath == "" ? config.Title : Path.GetFileName(physicalPath);
        directoryName = Path.GetInvalidFileNameChars()
            .Aggregate(directoryName,
                (current, invalid) => current.Replace(invalid, '_'));
        return new ZipDirectoryResult(zipService, physicalPath, directoryName + ".zip");
    }
}
