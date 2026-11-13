using Microsoft.AspNetCore.Mvc;

namespace notes.Pages.Editor;

/// <summary>Answers <c>?raw</c>: the bytes of a media file, to be shown inline by the file's page.</summary>
public sealed class RawHandler
{
    public IActionResult Handle(HttpResponse response, string physicalPath)
    {
        if (!File.Exists(physicalPath) || !TryPrepare(response, physicalPath, out string contentType))
            return new NotFoundResult();

        return new PhysicalFileResult(physicalPath, contentType)
        {
            EnableRangeProcessing = true // seeking in videos and audio
        };
    }

    /// <summary>The same for contents that aren't a file on disk (a file as an earlier state has it). <paramref name="name"/> says what type it is.</summary>
    public static IActionResult HandleContents(HttpResponse response, string name, byte[] contents)
        => TryPrepare(response, name, out string contentType)
            ? new FileContentResult(contents, contentType) { EnableRangeProcessing = true }
            : new NotFoundResult();

    /// <summary>The type the browser is told, from the name; false for what the browser isn't to show (see <see cref="MediaTypes"/>).</summary>
    private static bool TryPrepare(HttpResponse response, string name, out string contentType)
    {
        if (!MediaTypes.TryGet(name, out contentType, out _))
            return false;

        // The browser must not second-guess the type, e.g. by treating a renamed HTML file as a page of this site
        response.Headers.XContentTypeOptions = "nosniff";
        return true;
    }
}
