using System.IO.Compression;
using System.Text;
using Notes.Features.Components.Archive;
using Notes.Features.Components.Upload;
using Notes.Features.Helpers;

namespace Notes.Tests.Unit;

/// <summary>What an uploaded archive may hold to be unzipped (<see cref="UploadedArchives.Extract"/>): what can't be uploaded is refused.</summary>
public sealed class UploadedArchivesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "notes-upload-unzip", Guid.NewGuid().ToString("N"));

    public UploadedArchivesTests() => _ = Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            TestDirectories.Delete(_root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Staging => Path.Combine(_root, "staging");

    /// <summary>A zip with the entries as they are named, which the app itself would never write (leading out of it, say).</summary>
    private string ZipOf(params (string Name, string Contents)[] entries)
    {
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach ((string name, string contents) in entries)
            {
                using Stream stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(contents));
            }
        return path;
    }

    private int Extract(string archive, LimitsConfig? limits = null)
        => UploadedArchives.Extract(archive, ArchiveFormat.Zip, Staging, "upload.zip", limits ?? new LimitsConfig(), new UploadTexts(),
            new NameTexts(), CancellationToken.None);

    [Theory]
    [InlineData("../evil.md")]
    [InlineData("a/../../evil.md")]
    public void An_entry_that_leads_out_of_it_is_refused(string name)
    {
        UploadRefusedException refused = Assert.Throws<UploadRefusedException>(() => Extract(ZipOf((name, "x"))));

        Assert.Equal(400, refused.StatusCode);
        Assert.False(File.Exists(Path.Combine(_root, "evil.md")));
    }

    [Fact]
    public void More_than_may_be_unzipped_is_refused_by_what_is_written_not_by_what_it_says()
    {
        string archive = ZipOf(("a.md", new string('a', 600)), ("b.md", new string('b', 600)));

        UploadRefusedException refused = Assert.Throws<UploadRefusedException>(() => Extract(archive, new LimitsConfig { MaxUnzippedBytes = 1000 }));

        Assert.Equal(413, refused.StatusCode);
    }

    [Fact]
    public void More_entries_than_may_be_unzipped_are_refused()
    {
        string archive = ZipOf(("a.md", "a"), ("b.md", "b"), ("c.md", "c"));

        Assert.Equal(413, Assert.Throws<UploadRefusedException>(() => Extract(archive, new LimitsConfig { MaxUnzippedEntries = 2 })).StatusCode);
    }

    [Fact]
    public void A_name_that_can_not_be_an_entry_is_refused_and_reserved_ones_are_left_out()
    {
        Assert.Equal(400, Assert.Throws<UploadRefusedException>(() => Extract(ZipOf(("bad name.", "x")))).StatusCode);

        TestDirectories.Delete(Staging);
        int leftOut = Extract(ZipOf(("ok.md", "x"), ("_/history/HEAD", "x"), ("sub/.config.json", "{}"), ("sub/_.zip", "x")));

        Assert.Equal(3, leftOut);
        Assert.True(File.Exists(Path.Combine(Staging, "ok.md")));
        Assert.False(Directory.Exists(Path.Combine(Staging, "_")));
    }

    [Fact]
    public void An_entry_that_is_there_twice_is_refused()
        => Assert.Equal(400, Assert.Throws<UploadRefusedException>(() => Extract(ZipOf(("a.md", "1"), ("a.md", "2")))).StatusCode);

    [Fact]
    public void One_folder_around_all_of_it_is_left_out()
    {
        _ = Extract(ZipOf(("photos/a.jpg", "a"), ("photos/b/c.jpg", "c")));

        Assert.Equal(Path.Combine(Staging, "photos"), UploadedArchives.ContentsOf(Staging));
    }

    [Fact]
    public void Several_entries_at_the_top_are_kept_as_they_are()
    {
        _ = Extract(ZipOf(("photos/a.jpg", "a"), ("readme.md", "r")));

        Assert.Equal(Staging, UploadedArchives.ContentsOf(Staging));
    }

    [Fact]
    public void One_file_at_the_top_is_kept_as_it_is()
    {
        _ = Extract(ZipOf(("readme.md", "r")));

        Assert.Equal(Staging, UploadedArchives.ContentsOf(Staging));
    }
}
