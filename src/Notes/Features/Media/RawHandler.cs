using System.Security.Cryptography;
using ImageMagick;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Notes.Features.EntryActions;
using Notes.Features.ImageConversion;

namespace Notes.Features.Media;

/// <summary>
/// Answers <c>?raw</c>: the bytes of a media file, to be shown inline by the file's page. An image the browser can't show is converted
/// into one it can first (see <see cref="ConvertedImageFormats"/>); downloading it still gives the file as it is.
/// Any other file is refused (403, with the reason): sent as what it is, an HTML or SVG file of the entries would be a page of this site, with
/// its scripts, and for what isn't media there is nothing the browser would show better than the file's page (or a download) does.
/// </summary>
public sealed class RawHandler(Config config, MediaTexts texts, ILogger<RawHandler> logger) : IEntryGetAction
{
    public const string RawParameter = "raw";

    public string Parameter => RawParameter;
    public AccessNeeded Needs => AccessNeeded.Readable;

    public Task<IActionResult> GetAsync(EntryRequest request)
        => Task.FromResult(Raw(request.Response, request.PhysicalPath));

    private IActionResult Raw(HttpResponse response, string physicalPath)
    {
        if (!File.Exists(physicalPath))
            return new NotFoundResult();

        if (ConvertedImageFormats.TryGet(physicalPath, out MagickFormat format))
        {
            // Converting reads all of it into memory first
            if (new FileInfo(physicalPath).Length > config.Limits.MaxConvertedImageBytes)
                return new UnprocessableEntityResult();
            return Converted(response, physicalPath, ReadAllBytes(physicalPath), format, logger);
        }

        if (!TryPrepare(response, physicalPath, out string contentType))
            return NotMedia();
        return new PhysicalFileResult(physicalPath, contentType)
        {
            EnableRangeProcessing = true // seeking in videos and audio
        };
    }

    /// <summary>
    /// The same for contents that aren't a file on disk (a file as an earlier state has it). <paramref name="name"/> says what type it is.
    /// </summary>
    public IActionResult HandleContents(HttpResponse response, string name, byte[] contents)
    {
        if (ConvertedImageFormats.TryGet(name, out MagickFormat format))
        {
            if (contents.Length > response.HttpContext.RequestServices.GetRequiredService<Config>().Limits.MaxConvertedImageBytes)
                return new UnprocessableEntityResult();
            return Converted(response, name, contents, format, response.HttpContext.RequestServices.GetRequiredService<ILogger<RawHandler>>());
        }

        return TryPrepare(response, name, out string contentType)
            ? new FileContentResult(contents, contentType) { EnableRangeProcessing = true }
            : NotMedia();
    }

    /// <summary>
    /// The refusal of a file that isn't media. It has a text, which also keeps the error page away: that one would say that the file may not
    /// be read, which isn't so.
    /// </summary>
    private ContentResult NotMedia()
        => new() { StatusCode = StatusCodes.Status403Forbidden, ContentType = "text/plain; charset=utf-8", Content = texts.NotMedia };

    /// <summary>The type the browser is told, from the name; false for what the browser isn't to show (see <see cref="MediaTypes"/>).</summary>
    private static bool TryPrepare(HttpResponse response, string name, out string contentType)
    {
        if (!MediaTypes.TryGet(name, out contentType, out _))
            return false;

        ForbidSniffing(response);
        return true;
    }

    /// <summary>
    /// The image converted for the browser. Converting takes a while, so the answer is tagged with a hash of the contents it was made from:
    /// the browser asks again with it and gets a 304 without converting while the file is unchanged. Contents that can't be read as the
    /// image their name says answer 422, which the page shows as a file that can't be viewed, as do images too large to be converted.
    /// </summary>
    private static IActionResult Converted(HttpResponse response, string name, byte[] contents, MagickFormat format, ILogger logger)
    {
        ForbidSniffing(response);

        EntityTagHeaderValue tag = new('"' + Convert.ToHexString(SHA256.HashData(contents)) + '"');
        if (response.HttpContext.Request.GetTypedHeaders().IfNoneMatch.Any(asked => asked.Compare(tag, useStrongComparison: false)))
            return new StatusCodeResult(StatusCodes.Status304NotModified);

        ConvertedImage image;
        try
        {
            image = ImageForBrowser.Convert(contents, format);
        }
        catch (MagickException exception)
        {
            logger.LogWarning(exception, "{Name} could not be converted for the browser as {Format}.", name, format);
            return new UnprocessableEntityResult();
        }
        return new FileContentResult(image.Contents, image.ContentType) { EntityTag = tag };
    }

    // The browser must not second-guess the type, e.g. by treating a renamed HTML file as a page of this site
    private static void ForbidSniffing(HttpResponse response) => response.Headers.XContentTypeOptions = "nosniff";

    /// <summary>
    /// The whole file, also while something else has it open for writing or replaces it (a save, see <see cref="Editor.SaveHandler"/>).
    /// </summary>
    private static byte[] ReadAllBytes(string physicalPath)
    {
        using FileStream stream = new(physicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream contents = new();
        stream.CopyTo(contents);
        return contents.ToArray();
    }
}
