using System.IO.Compression;
using Microsoft.Playwright;

namespace notes.Tests.Ui;

/// <summary>
/// Takes a screenshot of every main view, on a desktop in the light theme and on a phone in the dark one, into docs/screenshots (the
/// documentation shows them). They are taken again with every run of the UI tests, so they always show the app as it is. A file whose
/// picture didn't change stays the same, and so gives no change in git.
/// </summary>
public sealed class ScreenshotTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private sealed record Variant(string Name, ViewportSize Viewport, ColorScheme Scheme);

    private static readonly Variant[] Variants =
    [
        new("desktop", new ViewportSize { Width = 1280, Height = 800 }, ColorScheme.Light),
        new("phone", new ViewportSize { Width = 390, Height = 844 }, ColorScheme.Dark)
    ];

    private const string Guide = """
        # Meeting notes

        Notes are **Markdown** files. The preview shows them rendered; the editor shows the text.

        ## Agenda

        1. Plans for the next weeks
        2. Open questions
        3. Anything else

        ## To do

        - [x] Book the room
        - [ ] Send the summary

        | Who   | What            |
        |-------|-----------------|
        | Alice | Writes the plan |
        | Carol | Reviews it      |

        ```csharp
        Console.WriteLine("Code blocks keep their formatting.");
        ```

        > Quotes, [links](https://example.com) and footnotes[^1] work as well.

        [^1]: Like this one.
        """;

    [Fact]
    public Task Directory()
        => CaptureAsync("directory", Users.Alice, "/");

    [Fact]
    public Task Directory_with_a_selection()
        => CaptureAsync("selection", Users.Alice, "/docs", async page =>
        {
            foreach (string path in new[] { "docs/a.md", "docs/nested" })
            {
                ILocator box = page.Locator($"tr.entry-row[data-path='{path}'] .entry-select");
                if (!await box.IsCheckedAsync())
                    await box.CheckAsync();
            }
            await Assertions.Expect(page.Locator("#selection-count")).ToHaveAttributeAsync("data-count", "2");
        });

    [Fact]
    public Task Markdown_preview()
    {
        Tree.File("docs/meeting.md", Guide);
        return CaptureAsync("markdown-preview", Users.Alice, "/docs/meeting.md");
    }

    [Fact]
    public Task Code_editor()
    {
        Tree.File("docs/meeting.md", Guide);
        return CaptureAsync("editor", Users.Alice, "/docs/meeting.md?edit", WaitForMonacoAsync);
    }

    [Fact]
    public Task Simple_editor()
    {
        Tree.File("docs/meeting.md", Guide);
        return CaptureAsync("simple-editor", Users.Alice, "/docs/meeting.md?edit", page => Assertions.Expect(page.Locator("#simple-editor")).ToBeVisibleAsync(),
            beforeLoad: "localStorage.setItem('notes.simpleEditor', 'true')");
    }

    [Fact]
    public Task Image()
    {
        Tree.File("docs/picture.png", GradientPng(640, 400));
        return CaptureAsync("image", Users.Alice, "/docs/picture.png", page => page.WaitForFunctionAsync("() => document.querySelector('img').complete"));
    }

    [Fact]
    public Task Permissions_editor()
        => CaptureAsync("permissions", Users.Admin, "/docs?config", WaitForMonacoAsync);

    [Fact]
    public Task Not_logged_in()
        => CaptureAsync("not-logged-in", Users.Anonymous, "/docs");

    [Fact]
    public Task Error_page()
        // The request id and the time change with every request
        => CaptureAsync("error", Users.Alice, "/no-such-file.md", hiddenCss: "main dl dd { visibility: hidden; }", expectOk: false);

    private static Task WaitForMonacoAsync(IPage page)
        => page.WaitForFunctionAsync("() => window.editor && typeof window.editor.getValue === 'function'");

    private async Task CaptureAsync(string name, TestUser user, string path, Func<IPage, Task>? prepare = null, string? beforeLoad = null,
        string? hiddenCss = null, bool expectOk = true)
    {
        foreach (Variant variant in Variants)
        {
            Dictionary<string, string> headers = [];
            if (user.ToHeader() is { } header)
                headers[TestUser.HeaderName] = header;
            await using IBrowserContext context = await Fixture.Browser!.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = Fixture.BaseUrl,
                ExtraHTTPHeaders = headers,
                ViewportSize = variant.Viewport,
                ColorScheme = variant.Scheme,
                DeviceScaleFactor = 1
            });
            context.SetDefaultTimeout(10_000);
            if (beforeLoad is not null)
                await context.AddInitScriptAsync(beforeLoad);

            IPage page = await context.NewPageAsync();
            IResponse? response = await page.GotoAsync(path);
            if (expectOk)
                Assert.True(response?.Ok, $"{path}: {response?.Status}");
            if (prepare is not null)
                await prepare(page);

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(ScreenshotDirectory, $"{name}-{variant.Name}.png"),
                Animations = ScreenshotAnimations.Disabled,
                Caret = ScreenshotCaret.Hide,
                Style = hiddenCss
            });
        }
    }

    /// <summary>docs/screenshots of the repository the tests run from.</summary>
    private static string ScreenshotDirectory
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "notes.sln")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            string screenshots = Path.Combine(directory.FullName, "docs", "screenshots");
            System.IO.Directory.CreateDirectory(screenshots);
            return screenshots;
        }
    }

    /// <summary>A picture to show in the image view, made here so that no binary file has to be kept for it.</summary>
    private static byte[] GradientPng(int width, int height)
    {
        byte[] pixels = new byte[height * (1 + width * 3)];
        for (int y = 0; y < height; y++)
        {
            int row = y * (1 + width * 3);
            pixels[row] = 0; // no filter
            for (int x = 0; x < width; x++)
            {
                pixels[row + 1 + x * 3] = (byte)(40 + 150 * x / width);
                pixels[row + 2 + x * 3] = (byte)(90 + 100 * y / height);
                pixels[row + 3 + x * 3] = 200;
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(pixels);

        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 2; // RGB
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        byte[] typeAndData = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        png.Write(typeAndData);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        png.Write(number);
    }

    /// <summary>The checksum of a PNG chunk (the one of zlib, gzip and PNG alike).</summary>
    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }
}
