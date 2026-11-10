using Microsoft.AspNetCore.Mvc;

namespace notes.Pages.Editor;

/// <summary>Answers <c>?raw</c>: the bytes of a media file, to be shown inline by the file's page.</summary>
public sealed class RawHandler
{
    public IActionResult Handle(HttpResponse response, string physicalPath)
    {
        if (!File.Exists(physicalPath) || !MediaTypes.TryGet(physicalPath, out string contentType, out _))
            return new NotFoundResult();

        // The browser must not second-guess the type, e.g. by treating a renamed HTML file as a page of this site
        response.Headers.XContentTypeOptions = "nosniff";

        return new PhysicalFileResult(physicalPath, contentType)
        {
            EnableRangeProcessing = true // seeking in videos and audio
        };
    }
}
