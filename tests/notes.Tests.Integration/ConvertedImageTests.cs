using System.Net;
using ImageMagick;

namespace notes.Tests.Integration;

/// <summary>Images the browser can't show are converted for <c>?raw</c>, and downloaded as they are.</summary>
public sealed class ConvertedImageTests(NotesAppFactory app) : IntegrationTest(app)
{
    private static byte[] Tiff()
    {
        using MagickImage image = new(MagickColors.Red, 32, 16) { Format = MagickFormat.Tiff };
        return image.ToByteArray();
    }

    [Fact]
    public async Task The_raw_bytes_are_a_jpeg_and_the_download_is_the_file_as_it_is()
    {
        byte[] tiff = Tiff();
        _ = Tree.File("docs/scan.tiff", tiff);

        HttpResponseMessage raw = await Client.GetAsync(Users.Alice, "/docs/scan.tiff?raw");
        await AssertStatus(HttpStatusCode.OK, raw);
        Assert.Equal("image/jpeg", raw.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", raw.Headers.GetValues("X-Content-Type-Options").Single());
        using (MagickImage shown = new(await raw.Content.ReadAsByteArrayAsync(Cancellation)))
            Assert.Equal(MagickFormat.Jpeg, shown.Format);

        HttpResponseMessage download = await Client.GetAsync(Users.Alice, "/docs/scan.tiff?download");
        Assert.Equal(tiff, await download.Content.ReadAsByteArrayAsync(Cancellation));

        // The page shows it as an image, from ?raw
        Assert.Contains("<img src=\"?raw\"", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/scan.tiff")));
    }

    [Fact]
    public async Task An_unchanged_file_is_not_converted_again_for_the_same_browser()
    {
        _ = Tree.File("docs/again.tiff", Tiff());
        HttpResponseMessage first = await Client.GetAsync(Users.Alice, "/docs/again.tiff?raw");
        string tag = first.Headers.ETag!.ToString();

        HttpResponseMessage unchanged = await Client.SendAsync(Users.Alice, HttpMethod.Get, "/docs/again.tiff?raw", headers: new Dictionary<string, string> { ["If-None-Match"] = tag });
        await AssertStatus(HttpStatusCode.NotModified, unchanged);

        _ = Tree.File("docs/again.tiff", Tiff().Concat(new byte[] { 0 }).ToArray());
        HttpResponseMessage changed = await Client.SendAsync(Users.Alice, HttpMethod.Get, "/docs/again.tiff?raw", headers: new Dictionary<string, string> { ["If-None-Match"] = tag });
        await AssertStatus(HttpStatusCode.OK, changed);
    }

    [Fact]
    public async Task A_file_that_is_not_the_image_its_name_says_cant_be_converted()
    {
        _ = Tree.File("docs/broken.heic", "not an image");

        await AssertStatus(HttpStatusCode.UnprocessableEntity, await Client.GetAsync(Users.Alice, "/docs/broken.heic?raw"));
    }
}
