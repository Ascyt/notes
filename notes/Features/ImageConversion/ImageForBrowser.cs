using ImageMagick;

namespace notes.Features.ImageConversion;

/// <summary>An image converted for the browser, and its media type.</summary>
public sealed record ConvertedImage(byte[] Contents, string ContentType);

/// <summary>Converts images the browser can't show (see <see cref="ConvertedImageFormats"/>) into JPEG, or PNG where they are transparent.</summary>
public static class ImageForBrowser
{
    /// <summary>How good the JPEG is: high enough that the conversion isn't noticed when looking at a photo.</summary>
    private const uint JpegQuality = 90;

    /// <summary>The largest width and height that is read, so that a small file claiming a huge image can't take up all memory.</summary>
    private const uint LargestSide = 30_000;

    /// <summary>
    /// How many images are converted at once: converting takes a processor (or several) for a while, so many requests at once would leave
    /// nothing for anything else. The rest wait.
    /// </summary>
    private static readonly SemaphoreSlim Converting = new(Math.Max(1, Environment.ProcessorCount / 2));

    static ImageForBrowser()
    {
        ResourceLimits.Width = LargestSide;
        ResourceLimits.Height = LargestSide;
    }

    /// <summary>Sets how many pixels an image may have to be converted (<see cref="notes.Features.Helpers.LimitsConfig.MaxConvertedImagePixels"/>). Once, at startup.</summary>
    public static void LimitPixels(ulong maxPixels)
        => ResourceLimits.Area = maxPixels;

    /// <summary>The image the contents are, read as <paramref name="format"/>, in a format the browser shows.</summary>
    /// <exception cref="MagickException">The contents aren't an image of that format, or one that can be read.</exception>
    public static ConvertedImage Convert(byte[] contents, MagickFormat format)
    {
        Converting.Wait();
        try
        {
            return ConvertNow(contents, format);
        }
        finally
        {
            _ = Converting.Release();
        }
    }

    /// <summary>
    /// A small picture of the image, for the grid of a directory's media: at most <see cref="ThumbnailSide"/> on its longer side, as WebP
    /// (which keeps transparency). Converted one at a time like the others, and read as <paramref name="format"/> only.
    /// </summary>
    /// <remarks>
    /// A directory asks for many at once, so the wait for a turn doesn't hold on to a thread (which would leave none for other requests);
    /// the making itself runs on a thread of the pool.
    /// </remarks>
    /// <exception cref="MagickException">The contents aren't an image of that format, or one that can be read.</exception>
    public static async Task<ConvertedImage> ThumbnailAsync(byte[] contents, MagickFormat format, CancellationToken cancellation)
    {
        await Converting.WaitAsync(cancellation);
        try
        {
            return await Task.Run(() => MakeThumbnail(contents, format), cancellation);
        }
        finally
        {
            _ = Converting.Release();
        }
    }

    private static ConvertedImage MakeThumbnail(byte[] contents, MagickFormat format)
    {
        MagickReadSettings settings = new() { Format = format, FrameIndex = 0, FrameCount = 1 };
        // A JPEG can be decoded at a fraction of its size right away, which is much faster for a photo than reading all of it
        if (format == MagickFormat.Jpeg)
            settings.SetDefine(MagickFormat.Jpeg, "size", $"{2 * ThumbnailSide}x{2 * ThumbnailSide}");
        using MagickImage image = new(contents, settings);
        image.AutoOrient();
        image.Thumbnail(new MagickGeometry(ThumbnailSide, ThumbnailSide) { Greater = true }); // keeps the proportions; only ever smaller, never larger
        image.Format = MagickFormat.WebP;
        image.Quality = ThumbnailQuality;
        return new ConvertedImage(image.ToByteArray(), "image/webp");
    }

    /// <summary>The longer side of a thumbnail: twice the size of a tile of the grid, so it is sharp on screens with more pixels.</summary>
    public const uint ThumbnailSide = 320;

    /// <summary>Good enough for a small picture, which is looked at for what it shows rather than for its details.</summary>
    private const uint ThumbnailQuality = 75;

    private static ConvertedImage ConvertNow(byte[] contents, MagickFormat format)
    {
        // Only the first frame: the merged image of a PSD, the first page of a TIFF
        MagickReadSettings settings = new() { Format = format, FrameIndex = 0, FrameCount = 1 };
        using MagickImage image = new(contents, settings);

        // Turned as the camera says, as the browser would do for a JPEG; the color profile is kept, which the browser applies
        image.AutoOrient();

        if (image.HasAlpha && !image.IsOpaque)
        {
            image.Format = MagickFormat.Png;
            return new ConvertedImage(image.ToByteArray(), "image/png");
        }

        image.Format = MagickFormat.Jpeg;
        image.Quality = JpegQuality;
        return new ConvertedImage(image.ToByteArray(), "image/jpeg");
    }
}
