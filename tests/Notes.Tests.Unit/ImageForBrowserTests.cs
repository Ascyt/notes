using ImageMagick;
using Notes.Features.ImageConversion;
using Notes.Features.Media;

namespace Notes.Tests.Unit;

/// <summary>Images the browser can't show, converted into ones it can.</summary>
public sealed class ImageForBrowserTests
{
    private static byte[] ImageOf(MagickFormat format, MagickColor color, uint width = 64, uint height = 48)
    {
        using MagickImage image = new(color, width, height) { Format = format };
        return image.ToByteArray();
    }

    [Theory]
    [InlineData("a.heic")]
    [InlineData("a.HEIF")]
    [InlineData("a.cr2")]
    [InlineData("a.tiff")]
    [InlineData("a.psd")]
    public void Images_the_browser_cant_show_are_shown_converted(string name)
    {
        Assert.Equal(MediaKind.Image, MediaTypes.KindOf(name));
        Assert.False(MediaTypes.TryGet(name, out _, out _)); // never sent as they are
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.jpg")]
    [InlineData("a.webp")]
    [InlineData("a.svg")] // read as SVG, it could reach other files and addresses
    [InlineData("a.md")]
    public void Everything_else_is_not_converted(string name)
        => Assert.False(ConvertedImageFormats.TryGet(name, out _));

    [Theory]
    [InlineData(MagickFormat.Tiff)]
    [InlineData(MagickFormat.Psd)]
    [InlineData(MagickFormat.Jxl)]
    [InlineData(MagickFormat.Jp2)]
    public void An_opaque_image_becomes_a_jpeg_of_the_same_size(MagickFormat format)
    {
        ConvertedImage converted = ImageForBrowser.Convert(ImageOf(format, MagickColors.Red), format);

        Assert.Equal("image/jpeg", converted.ContentType);
        using MagickImage result = new(converted.Contents);
        Assert.Equal(MagickFormat.Jpeg, result.Format);
        Assert.Equal((64u, 48u), (result.Width, result.Height));
    }

    [Fact]
    public void A_transparent_image_becomes_a_png()
    {
        ConvertedImage converted = ImageForBrowser.Convert(ImageOf(MagickFormat.Tiff, MagickColors.Transparent), MagickFormat.Tiff);

        Assert.Equal("image/png", converted.ContentType);
        using MagickImage result = new(converted.Contents);
        Assert.Equal(MagickFormat.Png, result.Format);
    }

    [Fact]
    public void Contents_are_read_as_the_format_of_the_name_not_as_what_they_claim()
    {
        // A PNG named .tiff isn't read as PNG (nor would anything else, such as a script for ImageMagick)
        byte[] png = ImageOf(MagickFormat.Png, MagickColors.Red);

        _ = Assert.ThrowsAny<MagickException>(() => ImageForBrowser.Convert(png, MagickFormat.Tiff));
    }

    [Fact]
    public void Contents_that_are_no_image_cant_be_converted()
        => Assert.ThrowsAny<MagickException>(() => ImageForBrowser.Convert("not an image"u8.ToArray(), MagickFormat.Heic));
}
