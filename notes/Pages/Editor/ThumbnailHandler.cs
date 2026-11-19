using System.Globalization;
using System.Security.Cryptography;
using ImageMagick;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using notes.Features.ImageConversion;

namespace notes.Pages.Editor;

/// <summary>
/// Answers <c>?thumbnail</c>: a small picture of an image, for the grid of media in its directory (see <see cref="ImageForBrowser.ThumbnailAsync"/>).
/// It is only made of images, read as the format their extension names; anything else is a 404, an image that can't be read or is too large
/// to be read a 422, which the grid shows as a tile with an icon instead.
/// </summary>
public sealed class ThumbnailHandler(Config config, ILogger<ThumbnailHandler> logger)
{
    public async Task<IActionResult> HandleAsync(HttpResponse response, string physicalPath)
    {
        FileInfo file = new(physicalPath);
        if (!file.Exists || !ConvertedImageFormats.TryGetForThumbnail(physicalPath, out MagickFormat format))
            return new NotFoundResult();
        if (file.Length > config.Limits.MaxConvertedImageBytes)
            return new UnprocessableEntityResult();

        // Tagged with the size and time of the file, so that an unchanged one isn't even read again: a directory shows many at once
        EntityTagHeaderValue tag = new('"' + file.Length.ToString(CultureInfo.InvariantCulture) + "-"
            + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "-" + ImageForBrowser.ThumbnailSide.ToString(CultureInfo.InvariantCulture) + '"');
        return await AnswerAsync(response, file.Name, tag, () => ReadAllBytes(physicalPath), format);
    }

    /// <summary>
    /// The same for contents that aren't a file on disk (a file as an earlier state has it), of <paramref name="size"/> bytes, which are only read
    /// if they aren't too large. <paramref name="name"/> says what type it is.
    /// </summary>
    public async Task<IActionResult> HandleContentsAsync(HttpResponse response, string name, long size, Func<byte[]> readContents)
    {
        if (!ConvertedImageFormats.TryGetForThumbnail(name, out MagickFormat format))
            return new NotFoundResult();
        if (size > config.Limits.MaxConvertedImageBytes)
            return new UnprocessableEntityResult();

        byte[] contents = readContents();
        EntityTagHeaderValue tag = new('"' + Convert.ToHexString(SHA256.HashData(contents)) + "-" + ImageForBrowser.ThumbnailSide.ToString(CultureInfo.InvariantCulture) + '"');
        return await AnswerAsync(response, name, tag, () => contents, format);
    }

    // The browser asks again with the tag, and gets a 304 without anything being made while the image is unchanged
    private async Task<IActionResult> AnswerAsync(HttpResponse response, string name, EntityTagHeaderValue tag, Func<byte[]> readContents, MagickFormat format)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        if (response.HttpContext.Request.GetTypedHeaders().IfNoneMatch.Any(asked => asked.Compare(tag, useStrongComparison: false)))
            return new StatusCodeResult(StatusCodes.Status304NotModified);

        ConvertedImage thumbnail;
        byte[] contents = readContents();
        format = ConvertedImageFormats.BrowserFormatOfContents(contents, format); // a WebP named .jpg is still a picture to the browser
        try
        {
            thumbnail = await ImageForBrowser.ThumbnailAsync(contents, format, response.HttpContext.RequestAborted);
        }
        catch (MagickException exception)
        {
            logger.LogWarning(exception, "No thumbnail could be made of {Name} as {Format}.", name, format);
            return new UnprocessableEntityResult();
        }
        return new FileContentResult(thumbnail.Contents, thumbnail.ContentType) { EntityTag = tag };
    }

    /// <summary>The whole file, also while something else has it open for writing.</summary>
    private static byte[] ReadAllBytes(string physicalPath)
    {
        using FileStream stream = new(physicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using MemoryStream contents = new();
        stream.CopyTo(contents);
        return contents.ToArray();
    }
}
