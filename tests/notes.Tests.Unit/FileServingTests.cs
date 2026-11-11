using System.IO.Compression;
using System.Text;
using notes.Features.Components.Download;
using notes.Pages.Editor;

namespace notes.Tests.Unit;

/// <summary>What files are sent and shown as: zips of directories, media, text or binary.</summary>
public sealed class FileServingTests : IDisposable
{
    private readonly TempNotes _notes = new();

    public void Dispose() => _notes.Dispose();

    private async Task<List<string>> ZipEntriesAsync(string directory, Func<FileSystemInfo, bool> include)
    {
        using MemoryStream output = new();
        await new ZipService().WriteZipAsync(ZipService.FolderOnDisk(_notes.P(directory), include), output, TestContext.Current.CancellationToken);
        output.Position = 0;
        using ZipArchive zip = new(output, ZipArchiveMode.Read);
        return zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task A_zip_has_the_tree_with_empty_directories_but_not_the_reserved_folder()
    {
        _ = _notes.Files.File("a.md", "a");
        _ = _notes.Files.File("sub/b.md", "b");
        _ = _notes.Files.Dir("empty");
        _ = _notes.Files.File("_/temp/x", "temp");

        Assert.Equal(["a.md", "empty/", "sub/b.md"], await ZipEntriesAsync("", _ => true));
    }

    [Fact]
    public async Task A_zip_leaves_out_what_is_not_included_and_does_not_look_inside_it()
    {
        _ = _notes.Files.File("open/a.md");
        _ = _notes.Files.File("closed/secret.md");
        List<string> asked = [];

        List<string> entries = await ZipEntriesAsync("", entry =>
        {
            asked.Add(entry.Name);
            return entry.Name != "closed";
        });

        Assert.Equal(["open/a.md"], entries);
        Assert.DoesNotContain("secret.md", asked);
    }

    [Fact]
    public async Task A_zip_does_not_follow_links()
    {
        string outside = Path.Combine(Path.GetTempPath(), "notes-unit-outside-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
        try
        {
            try
            {
                _ = Directory.CreateSymbolicLink(_notes.P("link"), outside);
                _ = File.CreateSymbolicLink(_notes.P("file-link.txt"), Path.Combine(outside, "secret.txt"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Symbolic links can't be made here (on Windows they need developer mode or admin rights).");
            }

            Assert.Empty(await ZipEntriesAsync("", _ => true));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task A_zip_of_files_with_impossible_dates_still_works()
    {
        string file = _notes.Files.File("old.md", "x");
        File.SetLastWriteTime(file, new DateTime(1970, 1, 2));

        Assert.Equal(["old.md"], await ZipEntriesAsync("", _ => true));
    }

    [Theory]
    [InlineData("a.png", MediaKind.Image)]
    [InlineData("a.JPG", MediaKind.Image)]
    [InlineData("a.mp4", MediaKind.Video)]
    [InlineData("a.mp3", MediaKind.Audio)]
    [InlineData("a.pdf", MediaKind.Pdf)]
    public void Media_the_browser_may_show(string name, MediaKind kind)
    {
        Assert.True(MediaTypes.TryGet(name, out string type, out MediaKind actual));
        Assert.Equal(kind, actual);
        Assert.NotEqual("", type);
    }

    [Theory]
    [InlineData("a.svg")] // can carry scripts
    [InlineData("a.html")]
    [InlineData("a.htm")]
    [InlineData("a.js")]
    [InlineData("a.xml")]
    [InlineData("a.xhtml")]
    [InlineData("a.md")]
    [InlineData("a")]
    [InlineData("a.exe")]
    public void Everything_else_is_never_served_inline(string name)
        => Assert.False(MediaTypes.TryGet(name, out _, out _));

    private async Task<FileView> ViewAsync(byte[] contents, string name = "f.md")
    {
        string path = _notes.Files.File(name, contents);
        FileView view = new() { VirtualPath = name, PhysicalPath = path };
        await view.InitAsync();
        return view;
    }

    [Fact]
    public async Task Text_is_shown_in_the_editor()
    {
        FileView view = await ViewAsync(Encoding.UTF8.GetBytes("# Hello ü 🎉"));

        Assert.False(view.IsBinary);
        Assert.Equal("# Hello ü 🎉", view.Contents);
        Assert.Equal("markdown", view.Language);
    }

    [Fact]
    public async Task Only_markdown_has_a_preview_which_whoever_may_not_write_can_not_leave()
    {
        async Task<FileView> Open(string name, bool canWrite, bool edit)
        {
            string path = _notes.Files.File(name, "# T");
            FileView view = new() { VirtualPath = name, PhysicalPath = path, CanWrite = canWrite, EditRequested = edit };
            await view.InitAsync();
            return view;
        }

        Assert.True((await Open("a.md", canWrite: true, edit: false)).ShowsPreview);
        Assert.Contains("<h1", (await Open("b.MD", canWrite: true, edit: false)).Preview.Html);
        Assert.False((await Open("c.md", canWrite: true, edit: true)).ShowsPreview);
        Assert.True((await Open("d.md", canWrite: false, edit: true)).ShowsPreview);
        Assert.False((await Open("e.txt", canWrite: true, edit: false)).ShowsPreview);
        Assert.False((await Open("f.json", canWrite: false, edit: false)).ShowsPreview);
        Assert.Equal("", (await Open("g.txt", canWrite: true, edit: false)).Preview.Html);
    }

    [Fact]
    public async Task A_nul_byte_or_invalid_utf8_makes_a_file_binary()
    {
        Assert.True((await ViewAsync([0x41, 0x00, 0x42])).IsBinary);
        Assert.True((await ViewAsync([0x41, 0xC3, 0x28])).IsBinary);
        Assert.Equal(MediaKind.Image, (await ViewAsync([0x89, 0x50, 0x4E, 0x47, 0x00], "x.png")).Media);
    }

    [Fact]
    public async Task A_character_cut_at_the_end_of_the_sniffed_part_is_not_binary()
    {
        // 7999 ASCII bytes, then "ü" (2 bytes) across the 8000 byte boundary, then more text
        byte[] contents = [.. Enumerable.Repeat((byte)'a', 7999), 0xC3, 0xBC, (byte)'b'];

        Assert.False((await ViewAsync(contents)).IsBinary);
    }

    [Fact]
    public async Task Sizes_are_shown_in_binary_units()
    {
        Assert.Equal("3 B", (await ViewAsync([0, 1, 2])).FormattedSize);
        Assert.Equal("1.5 KiB", (await ViewAsync(new byte[1536])).FormattedSize);
    }

    [Fact]
    public async Task Synchronous_writes_are_held_back_until_the_next_asynchronous_one()
    {
        using MemoryStream inner = new();
        DeferredWriteStream stream = new(inner);

        stream.Write([1, 2]);
        Assert.Equal(0, inner.Length);
        await stream.WriteAsync(new byte[] { 3 }, TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3], inner.ToArray());
        stream.Write([4]);
        await stream.FlushDeferredAsync(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3, 4], inner.ToArray());
    }

    [Fact]
    public void Too_much_synchronous_writing_is_refused_instead_of_buffered()
    {
        DeferredWriteStream stream = new(Stream.Null);

        Assert.Throws<InvalidOperationException>(() => stream.Write(new byte[DeferredWriteStream.MaxDeferredBytes + 1]));
    }
}
