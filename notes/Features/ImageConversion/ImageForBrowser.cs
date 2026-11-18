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

    static ImageForBrowser()
    {
        ResourceLimits.Width = LargestSide;
        ResourceLimits.Height = LargestSide;
    }

    /// <summary>The image the contents are, read as <paramref name="format"/>, in a format the browser shows.</summary>
    /// <exception cref="MagickException">The contents aren't an image of that format, or one that can be read.</exception>
    public static ConvertedImage Convert(byte[] contents, MagickFormat format)
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
