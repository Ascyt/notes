using System.Net;
using System.Text.RegularExpressions;
using ImageMagick;

namespace Notes.Tests.Integration;

/// <summary>The media of a directory: their grid in the listing, the thumbnails of images (<c>?thumbnail</c>), and the way from one to the next.</summary>
public sealed class MediaTests(NotesAppFactory app) : IntegrationTest(app)
{
    private static byte[] Png(uint width, uint height)
    {
        using MagickImage image = new(MagickColors.Blue, width, height) { Format = MagickFormat.Png };
        return image.ToByteArray();
    }

    // ---- Thumbnails

    [Fact]
    public async Task The_thumbnail_of_an_image_is_a_small_webp_of_the_same_proportions()
    {
        _ = Tree.File("docs/wide.png", Png(1000, 500));

        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/docs/wide.png?thumbnail");
        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        using MagickImage thumbnail = new(await response.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal(MagickFormat.WebP, thumbnail.Format);
        Assert.Equal(320u, thumbnail.Width);
        Assert.Equal(160u, thumbnail.Height);
    }

    [Fact]
    public async Task A_small_image_is_not_made_larger()
    {
        _ = Tree.File("docs/small.png", Png(40, 20));
        using MagickImage thumbnail = new(await (await Client.GetAsync(Users.Alice, "/docs/small.png?thumbnail")).Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal(40u, thumbnail.Width);
    }

    [Fact]
    public async Task An_unchanged_image_is_not_made_into_a_thumbnail_again_for_the_same_browser()
    {
        _ = Tree.File("docs/again.png", Png(100, 100));
        HttpResponseMessage first = await Client.GetAsync(Users.Alice, "/docs/again.png?thumbnail");
        string tag = first.Headers.ETag!.ToString();
        Dictionary<string, string> headers = new() { ["If-None-Match"] = tag };

        await AssertStatus(HttpStatusCode.NotModified, await Client.SendAsync(Users.Alice, HttpMethod.Get, "/docs/again.png?thumbnail", headers: headers));

        _ = Tree.File("docs/again.png", Png(100, 50));
        await AssertStatus(HttpStatusCode.OK, await Client.SendAsync(Users.Alice, HttpMethod.Get, "/docs/again.png?thumbnail", headers: headers));
    }

    [Fact]
    public async Task There_are_thumbnails_only_of_images_that_can_be_read_as_what_their_name_says()
    {
        _ = Tree.File("docs/clip.mp4", [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70]);
        _ = Tree.File("docs/drawing.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        _ = Tree.File("docs/broken.png", "not an image");
        // An SVG named as a JPEG is no image the browser shows, and is never read as an SVG
        _ = Tree.File("docs/renamed.jpg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/a.md?thumbnail"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/clip.mp4?thumbnail"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/drawing.svg?thumbnail"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs?thumbnail"));
        await AssertStatus(HttpStatusCode.UnprocessableEntity, await Client.GetAsync(Users.Alice, "/docs/broken.png?thumbnail"));
        await AssertStatus(HttpStatusCode.UnprocessableEntity, await Client.GetAsync(Users.Alice, "/docs/renamed.jpg?thumbnail"));
    }

    [Fact]
    public async Task An_image_the_browser_shows_has_a_thumbnail_also_with_the_wrong_extension()
    {
        // Saved from the web: a WebP named .jpg, which the browser shows all the same
        using MagickImage webp = new(MagickColors.Red, 64, 32) { Format = MagickFormat.WebP };
        _ = Tree.File("docs/saved.jpg", webp.ToByteArray());
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/saved.jpg?thumbnail"));

        // Only between the formats the browser shows: a converted format is read as its name says, whatever it starts with
        _ = Tree.File("docs/saved.heic", webp.ToByteArray());
        await AssertStatus(HttpStatusCode.UnprocessableEntity, await Client.GetAsync(Users.Alice, "/docs/saved.heic?thumbnail"));
    }

    [Fact]
    public async Task Images_the_browser_cant_show_have_thumbnails_too()
    {
        using MagickImage tiff = new(MagickColors.Red, 64, 32) { Format = MagickFormat.Tiff };
        _ = Tree.File("docs/scan.tiff", tiff.ToByteArray());

        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/docs/scan.tiff?thumbnail");
        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- The listing

    [Fact]
    public async Task Images_videos_and_audio_are_listed_as_media_and_not_among_the_files()
    {
        _ = Tree.File("docs/photo.png", Png(10, 10));
        _ = Tree.File("docs/clip.mp4", [0]);
        _ = Tree.File("docs/sound.mp3", [0]);

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Contains("id=\"media-group\"", body);
        Assert.Contains("data-group=\"media\" data-path=\"docs/photo.png\"", body);
        Assert.Contains("data-group=\"media\" data-path=\"docs/clip.mp4\"", body);
        Assert.Contains("src=\"/docs/photo.png?thumbnail\"", body);
        Assert.Contains("src=\"/docs/clip.mp4?raw#t=0.5\"", body);
        // Audio has no picture, only the icon of its kind
        Assert.Contains("data-group=\"media\" data-path=\"docs/sound.mp3\"", body);
        Assert.DoesNotContain("src=\"/docs/sound.mp3", body);
        Assert.DoesNotContain("data-group=\"files\" data-path=\"docs/photo.png\"", body);
    }

    [Fact]
    public async Task An_empty_group_is_only_its_button_to_add_without_header()
    {
        _ = Tree.File("docs/only/a.md", "x");

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/only"));
        Assert.Matches(@"id=""directories-group""(?:(?!</table>)[\s\S])*create=directory", body);
        Assert.DoesNotMatch(@"id=""directories-group""(?:(?!</table>)[\s\S])*<thead>", body);
        Assert.Matches(@"id=""files-group""(?:(?!</table>)[\s\S])*<thead>", body);

        // With nothing to add either, the group is left out; with nothing at all, the directory says it is empty
        string readOnly = await BodyOf(await Client.GetAsync(Users.Anonymous, "/docs/only"));
        Assert.DoesNotContain("id=\"directories-group\"", readOnly);
        Assert.Contains("id=\"files-group\"", readOnly);
        Assert.DoesNotContain("id=\"empty-directory\"", readOnly);
    }

    [Fact]
    public async Task Without_media_there_is_no_grid()
        => Assert.DoesNotContain("id=\"media-group\"", await BodyOf(await Client.GetAsync(Users.Alice, "/docs")));

    [Fact]
    public async Task Media_have_alt_and_the_number_as_shortcut_and_alt_shift_to_select()
    {
        _ = Tree.File("docs/photo.png", Png(10, 10));
        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Matches(@"<a class=""media-link[^""]*"" href=""/docs/photo.png"" key=""!1""", body);
        Assert.Matches(@"class=""form-check-input entry-select media-select"" key=""!(\+|&#x2B;)1""", body); // Razor may write the + as an entity
    }

    [Fact]
    public async Task The_numbers_of_a_group_have_as_many_digits_as_its_largest()
    {
        for (int i = 0; i < 10; i++)
            _ = Tree.File($"docs/many/file{i}.md", "x");

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/many"));
        string[] numbers = [.. Regex.Matches(body, @"<span class=""entry-index"">(\d+)</span>").Select(match => match.Groups[1].Value)];
        Assert.Equal(["01", "02", "03", "04", "05", "06", "07", "08", "09", "10"], numbers);
    }

    // ---- From one to the next

    [Fact]
    public async Task A_media_file_leads_to_the_media_next_to_it_in_the_grid()
    {
        // In the order of the listing: by name, the last first
        _ = Tree.File("docs/pics/a.png", Png(10, 10));
        _ = Tree.File("docs/pics/b.mp4", [0]);
        _ = Tree.File("docs/pics/c.png", Png(10, 10));
        _ = Tree.File("docs/pics/notes.md", "not media");

        string middle = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/pics/b.mp4"));
        Assert.Matches(@"<a id=""previous-media-button"" href=""/docs/pics/c.png""", middle);
        Assert.Matches(@"<a id=""next-media-button"" href=""/docs/pics/a.png""", middle);

        // The first can't go back, the last can't go on; both say why
        string first = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/pics/c.png"));
        Assert.Matches(@"<button id=""previous-media-button""[^>]*data-disabled-reason=""\S", first);
        Assert.Matches(@"<a id=""next-media-button"" href=""/docs/pics/b.mp4""", first);
        string last = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/pics/a.png"));
        Assert.Matches(@"<button id=""next-media-button""[^>]*data-disabled-reason=""\S", last);

        Assert.DoesNotContain("previous-media-button", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/pics/notes.md")));
    }

    [Fact]
    public async Task Media_that_cant_be_opened_are_skipped()
    {
        _ = Tree.File("docs/pics/a.png", Png(10, 10));
        _ = Tree.File("docs/pics/b.png", Png(10, 10));
        _ = Tree.File("docs/pics/c.png", Png(10, 10));
        Tree.Config("docs/pics", Cfg.Folder(Cfg.Perms(), ("b.png", Cfg.Perms(rwDeny: ["a"]))));

        string first = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/pics/c.png"));
        Assert.Matches(@"<a id=""next-media-button"" href=""/docs/pics/a.png""", first);
        // Still listed, locked, as the other entries that can't be opened
        Assert.Contains("data-locked-entry=\"b.png\"", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/pics")));
    }
}
