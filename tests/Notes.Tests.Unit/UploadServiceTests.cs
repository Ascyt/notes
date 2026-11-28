using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Notes.Features.Auth;
using Notes.Features.Components.Upload;
using Notes.Features.Helpers;

namespace Notes.Tests.Unit;

public sealed class UploadServiceTests : IDisposable
{
    private readonly TempNotes _notes = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly UploadService _uploads;

    public UploadServiceTests()
    {
        _uploads = new UploadService(_notes.Locations, _notes.Paths, new StructureLock(), _notes.Store, new LimitsConfig(), _time,
            new UploadTexts(), new NameTexts(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadService>.Instance);
        _ = _notes.Files.Dir("dir");
    }

    public void Dispose() => _notes.Dispose();

    private static MemoryStream Body(string text) => new(Encoding.UTF8.GetBytes(text));

    private Task<string?> Chunk(string user, string id, string name, long offset, bool last, string text, Func<string, bool>? mayCreate = null,
        string[]? folders = null, Func<string, bool>? mayEnter = null)
        => _uploads.UploadChunkAsync(user, Guid.Parse(id), _notes.P("dir"), folders ?? [], name, offset, last, Body(text), mayCreate ?? (_ => true),
            mayEnter ?? (_ => true));

    [Fact]
    public async Task Folders_that_are_not_there_are_made_with_the_last_chunk()
    {
        _ = _notes.Files.Dir("dir/Assets");

        string id = _uploads.StartUpload("u");
        Assert.Equal("", await Chunk("u", id, "a.png", 0, false, "ab", folders: ["assets", "new"]));
        Assert.False(Directory.Exists(_notes.P("dir/Assets/new")));
        // The folder that is there is found in any case
        Assert.Equal("dir/Assets/new/a.png", await Chunk("u", id, "a.png", 2, true, "c", folders: ["assets", "new"]));
        Assert.Equal("abc", _notes.Files.Read("dir/Assets/new/a.png"));
    }

    [Fact]
    public async Task A_folder_that_may_not_be_made_or_entered_discards_the_upload()
    {
        _ = _notes.Files.Dir("dir/locked");
        _ = _notes.Files.File("dir/file.md", "");

        string id = _uploads.StartUpload("u");
        _ = await Assert.ThrowsAsync<AccessDeniedException>(() => Chunk("u", id, "a.png", 0, true, "x", folders: ["new"],
            mayCreate: path => !path.EndsWith("new")));
        Assert.False(Directory.Exists(_notes.P("dir/new")));

        id = _uploads.StartUpload("u");
        _ = await Assert.ThrowsAsync<AccessDeniedException>(() => Chunk("u", id, "a.png", 0, true, "x", folders: ["locked"],
            mayEnter: _ => false));

        id = _uploads.StartUpload("u");
        _ = await Assert.ThrowsAsync<UploadRefusedException>(() => Chunk("u", id, "a.png", 0, true, "x", folders: ["file.md"]));
        Assert.Empty(Directory.EnumerateFiles(TempDirectoryOf("u")));
    }

    private string TempDirectoryOf(string user) => Path.Combine(_notes.Root, "_", "temp", "user", user);

    [Fact]
    public void Uploads_in_chunks_and_never_overwrites()
    {
        _ = _notes.Files.File("dir/a.md", "old");

        string id = _uploads.StartUpload("u");
        Assert.Equal("", Chunk("u", id, "a.md", 0, false, "hello ").Result);
        Assert.Equal("dir/a (1).md", Chunk("u", id, "a.md", 6, true, "world").Result);

        Assert.Equal("old", _notes.Files.Read("dir/a.md"));
        Assert.Equal("hello world", _notes.Files.Read("dir/a (1).md"));
        Assert.Empty(Directory.EnumerateFiles(TempDirectoryOf("u")));
    }

    [Fact]
    public async Task A_retried_chunk_overwrites_but_a_gap_is_refused()
    {
        string id = _uploads.StartUpload("u");
        _ = await Chunk("u", id, "a.md", 0, false, "abc");

        Assert.Null(await Chunk("u", id, "a.md", 10, false, "gap"));
        Assert.Equal("", await Chunk("u", id, "a.md", 1, false, "XY"));
        Assert.Equal("dir/a.md", await Chunk("u", id, "a.md", 3, true, "!"));
        Assert.Equal("aXY!", _notes.Files.Read("dir/a.md"));
    }

    [Fact]
    public async Task The_upload_of_another_user_can_not_be_continued()
    {
        string id = _uploads.StartUpload("alice");

        Assert.Null(await Chunk("mallory", id, "a.md", 0, true, "evil"));
        Assert.False(_notes.Files.Exists("dir/a.md"));
    }

    [Fact]
    public async Task An_id_that_was_never_started_finds_nothing()
        => Assert.Null(await Chunk("u", Guid.NewGuid().ToString("N"), "a.md", 0, true, "x"));

    [Fact]
    public async Task The_final_name_is_asked_about_and_a_refusal_discards_the_upload()
    {
        _ = _notes.Files.File("dir/a.md");
        string id = _uploads.StartUpload("u");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Chunk("u", id, "a.md", 0, true, "x", mayCreate: path => !path.EndsWith("a (1).md", StringComparison.Ordinal)));

        Assert.False(_notes.Files.Exists("dir/a (1).md"));
        Assert.Empty(Directory.EnumerateFiles(TempDirectoryOf("u")));
    }

    [Fact]
    public async Task Aborting_discards_what_was_uploaded()
    {
        string id = _uploads.StartUpload("u");
        _ = await Chunk("u", id, "a.md", 0, false, "x");

        _uploads.AbortUpload("u", Guid.Parse(id));

        Assert.Null(await Chunk("u", id, "a.md", 1, true, "y"));
    }

    [Theory]
    [InlineData("../../escape")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("C:\\x")]
    [InlineData("")]
    public async Task User_ids_that_are_not_safe_folder_names_are_hashed(string user)
    {
        string id = _uploads.StartUpload(user);

        string[] folders = Directory.GetDirectories(Path.Combine(_notes.Root, "_", "temp", "user")).Select(Path.GetFileName).ToArray()!;
        string folder = Assert.Single(folders);
        Assert.Matches("^[0-9A-F]{64}$", folder);
        Assert.Equal("dir/a.md", await Chunk(user, id, "a.md", 0, true, "x"));
    }

    // ---- The job that clears what abandoned uploads leave behind

    [Fact]
    [Trait("Category", "Timing")]
    public async Task Stale_uploads_are_deleted_after_an_hour_and_recent_ones_kept()
    {
        string old = _uploads.StartUpload("u");
        _ = await Chunk("u", old, "a.md", 0, false, "x");

        _time.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, _uploads.DeleteStaleUploads(TimeSpan.FromHours(1)));

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, _uploads.DeleteStaleUploads(TimeSpan.FromHours(1)));
        Assert.Null(await Chunk("u", old, "a.md", 1, true, "y"));
        // The empty folder of the user goes as well
        Assert.False(Directory.Exists(TempDirectoryOf("u")));
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void The_job_runs_with_an_hour()
    {
        _ = _uploads.StartUpload("u");
        ClearStaleUploadsJob job = new(_uploads, NullLogger<ClearStaleUploadsJob>.Instance);

        job.Run();
        Assert.Single(Directory.EnumerateFiles(TempDirectoryOf("u")));

        _time.Advance(TimeSpan.FromMinutes(61));
        job.Run();
        Assert.False(Directory.Exists(TempDirectoryOf("u")));
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void Nothing_to_clear()
        => Assert.Equal(0, _uploads.DeleteStaleUploads(TimeSpan.FromHours(1)));
}
