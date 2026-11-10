using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using notes.Features.Components.Upload;
using notes.Features.Helpers;

namespace notes.Tests.Integration;

/// <summary>
/// What happens when time runs out while something is going on: the job that clears abandoned uploads, the lifetime of selections, and the
/// notes changing under a running download. The app's clock is moved by the test (no waiting), and operations are held half way where needed.
/// Each test has an app of its own, as the clock only moves forward. Skip them with <c>--filter-not-trait "Category=Timing"</c>.
/// </summary>
[Trait("Category", "Timing")]
public abstract class TimingTest() : IntegrationTest(new NotesAppFactory()), IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        App.BeforeAccessCheck = null;
        await App.DisposeAsync();
    }

    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Holds the lock of all structure changes, so that the next one waits for it, half way.</summary>
    protected Task<IDisposable> HoldStructureLockAsync() => App.Services.GetRequiredService<StructureLock>().EnterAsync();

    protected void RunStaleUploadsJob()
    {
        using IServiceScope scope = App.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClearStaleUploadsJob>().Run();
    }

    protected string UploadTempDirectory(TestUser user) => Path.Combine(App.NotesDir, "_", "temp", "user", user.Sub!);

    protected static async Task Eventually(Func<bool> condition, string what)
    {
        DateTime end = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                Assert.Fail("Timed out waiting until " + what);
            await Task.Delay(20, Cancellation);
        }
    }

    protected async Task<string> StartUploadAsync(TestUser user, string directory)
    {
        HttpResponseMessage response = await Client.PostAsync(user, "/" + directory + "?upload=start");
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    protected Task<HttpResponseMessage> ChunkAsync(TestUser user, string directory, string id, string name, long offset, bool last, HttpContent content)
        => Client.SendAsync(user, HttpMethod.Post, $"/{directory}?upload={id}&name={Uri.EscapeDataString(name)}&offset={offset}" + (last ? "&last" : ""), content);

    protected Task<HttpResponseMessage> ChunkAsync(TestUser user, string directory, string id, string name, long offset, bool last, string contents)
        => ChunkAsync(user, directory, id, name, offset, last, new ByteArrayContent(Encoding.UTF8.GetBytes(contents)));
}

/// <summary>The hourly job that deletes uploads nobody wrote to for an hour.</summary>
public sealed class StaleUploadTimingTests : TimingTest
{
    [Fact]
    public async Task An_upload_that_was_abandoned_for_more_than_an_hour_is_cleared_and_can_not_be_continued()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");
        await AssertStatus(HttpStatusCode.OK, await ChunkAsync(Users.Alice, "docs", id, "big.md", 0, false, "first part "));

        App.Time.Advance(TimeSpan.FromMinutes(61));
        RunStaleUploadsJob();

        // The page gets a 409, says the upload failed, and offers to upload again
        await AssertStatus(HttpStatusCode.Conflict, await ChunkAsync(Users.Alice, "docs", id, "big.md", 11, true, "second part"));
        Assert.False(Tree.Exists("docs/big.md"));
        Assert.False(Directory.Exists(UploadTempDirectory(Users.Alice)));
    }

    [Fact]
    public async Task An_upload_that_is_slow_but_going_on_is_kept()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");
        _ = await ChunkAsync(Users.Alice, "docs", id, "slow.md", 0, false, "a");

        App.Time.Advance(TimeSpan.FromMinutes(45));
        RunStaleUploadsJob();
        _ = await ChunkAsync(Users.Alice, "docs", id, "slow.md", 1, false, "b");

        App.Time.Advance(TimeSpan.FromMinutes(45));
        RunStaleUploadsJob();
        HttpResponseMessage last = await ChunkAsync(Users.Alice, "docs", id, "slow.md", 2, true, "c");

        await AssertStatus(HttpStatusCode.OK, last);
        Assert.Equal("abc", Tree.Read("docs/slow.md"));
    }

    [Fact]
    public async Task The_job_firing_while_a_chunk_is_being_written_leaves_no_half_file_and_no_server_error()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");
        BlockingContent content = new(Encoding.UTF8.GetBytes(new string('x', 256 * 1024)), Encoding.UTF8.GetBytes("end"));
        Task<HttpResponseMessage> chunk = ChunkAsync(Users.Alice, "docs", id, "racing.md", 0, true, content);
        string temp = Path.Combine(UploadTempDirectory(Users.Alice), id + ".part");
        await Eventually(() => File.Exists(temp) && new FileInfo(temp).Length > 0, "the first part of the chunk is written");

        App.Time.Advance(TimeSpan.FromHours(2));
        RunStaleUploadsJob();
        content.Release();
        HttpResponseMessage response = await chunk.WaitAsync(Timeout, Cancellation);

        // Either the upload made it (the file was in use, so the job left it) or it is refused as gone; never a broken file or a crash
        if (response.StatusCode == HttpStatusCode.OK)
            Assert.Equal(256 * 1024 + 3, new FileInfo(Tree.Physical("docs/racing.md")).Length);
        else
        {
            await AssertStatus(HttpStatusCode.Conflict, response);
            Assert.False(Tree.Exists("docs/racing.md"));
        }
    }

    /// <summary>
    /// The last chunk is written, and the upload waits for the structure lock to put the file in place (another upload, a copy, ... is
    /// running). If the job clears the upload meanwhile, the upload must fail as gone, like any upload the job cleared, not with a server error.
    /// </summary>
    [Fact]
    public async Task The_job_firing_while_a_finished_upload_waits_to_be_put_in_place_is_no_server_error()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");
        string temp = Path.Combine(UploadTempDirectory(Users.Alice), id + ".part");
        Task<HttpResponseMessage> last;
        using (await HoldStructureLockAsync())
        {
            last = ChunkAsync(Users.Alice, "docs", id, "waiting.md", 0, true, "all of it");
            await Eventually(() => new FileInfo(temp) is { Exists: true, Length: 9 }, "the last chunk is written");
            await Task.Delay(200, Cancellation); // ...and the upload waits for the lock

            App.Time.Advance(TimeSpan.FromHours(2));
            RunStaleUploadsJob();
            Assert.False(File.Exists(temp));
        }

        HttpResponseMessage response = await last.WaitAsync(Timeout, Cancellation);
        Assert.False(Tree.Exists("docs/waiting.md"));
        await AssertStatus(HttpStatusCode.Conflict, response);
    }

    /// <summary>A request body that sends its first part, then waits until <see cref="Release"/> before sending the rest.</summary>
    private sealed class BlockingContent(byte[] first, byte[] rest) : HttpContent
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _released.TrySetResult();

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await stream.WriteAsync(first);
            await stream.FlushAsync();
            await _released.Task;
            await stream.WriteAsync(rest);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = first.Length + rest.Length;
            return true;
        }
    }
}

/// <summary>Selections are forgotten after 30 minutes without use.</summary>
public sealed class SelectionTimingTests : TimingTest
{
    [Fact]
    public async Task A_selection_left_alone_for_half_an_hour_is_forgotten()
    {
        _ = await SelectAsync(Users.Alice, "docs/a.md");

        App.Time.Advance(TimeSpan.FromMinutes(31));

        Assert.Empty((await GetSelectionAsync(Users.Alice)).Paths);
    }

    [Fact]
    public async Task Looking_at_the_selection_keeps_it()
    {
        _ = await SelectAsync(Users.Alice, "docs/a.md");

        for (int i = 0; i < 3; i++)
        {
            App.Time.Advance(TimeSpan.FromMinutes(25));
            // Every page of a directory looks at it
            await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs"));
        }

        Assert.Equal(["docs/a.md"], (await GetSelectionAsync(Users.Alice)).Paths);
    }

    [Fact]
    public async Task A_page_that_still_shows_a_forgotten_selection_can_not_transfer_it()
    {
        SelectionSnapshotDto shown = await SelectAsync(Users.Alice, "docs/a.md", "docs/nested");
        string before = Tree.Snapshot();

        App.Time.Advance(TimeSpan.FromMinutes(31));
        HttpResponseMessage response = await Client.PostJsonAsync(Users.Alice, "/team?transfer=move", new { version = shown.Version });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task A_forgotten_selection_is_a_fresh_one_that_works_as_usual()
    {
        _ = await SelectAsync(Users.Alice, "docs/a.md");
        App.Time.Advance(TimeSpan.FromHours(5));

        SelectionSnapshotDto fresh = await SelectAsync(Users.Alice, "docs/nested/b.md");
        HttpResponseMessage response = await Client.PostJsonAsync(Users.Alice, "/team?transfer=copy", new { version = fresh.Version });

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.True(Tree.Exists("team/b.md"));
        Assert.False(Tree.Exists("team/a.md"));
    }

    /// <summary>
    /// A long copy or move: nothing looks at the selection while it runs, but it must not be forgotten while it is busy. Otherwise the user
    /// (another tab) could select again and start a second transfer, and the end of the first one would clear what they selected meanwhile.
    /// </summary>
    [Fact]
    public async Task A_selection_is_not_forgotten_while_it_is_being_transferred()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md");
        Task<HttpResponseMessage> transfer;
        HttpResponseMessage meanwhile;
        using (await HoldStructureLockAsync())
        {
            transfer = Client.PostJsonAsync(Users.Alice, "/team?transfer=copy", new { version = selection.Version });
            await Eventually(() => App.Services.GetRequiredService<notes.Features.Components.Selection.ISelectionService>().Get(Users.Alice.Sub!).Busy, "the transfer has started");

            App.Time.Advance(TimeSpan.FromMinutes(31));
            meanwhile = await Client.PostJsonAsync(Users.Alice, "/?selection=add", new { paths = new[] { "docs/nested" } });
        }

        await AssertStatus(HttpStatusCode.OK, await transfer.WaitAsync(Timeout, Cancellation));
        await AssertStatus(HttpStatusCode.Conflict, meanwhile);
    }
}

/// <summary>A zip download is made while it is sent, so the notes can change (and time pass) while it runs.</summary>
public sealed class DownloadTimingTests : TimingTest
{
    /// <summary>Holds the zip of alice's download when it asks about <paramref name="path"/>, until the returned release is called.</summary>
    private (Task Reached, Action Release) PauseZipAt(string path)
    {
        string physical = Tree.Physical(path);
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ManualResetEventSlim release = new();
        App.BeforeAccessCheck = (user, asked) =>
        {
            if (user.FindFirst("sub")?.Value == Users.Alice.Sub && string.Equals(asked, physical, StringComparison.OrdinalIgnoreCase) && reached.TrySetResult())
                _ = release.Wait(Timeout);
        };
        return (reached.Task, release.Set);
    }

    /// <summary>The answer and the names in the zip; no names if the download broke off.</summary>
    private static async Task<(HttpStatusCode? Status, List<string>? Names)> DownloadAsync(Task<HttpResponseMessage> download)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await download.WaitAsync(Timeout, Cancellation);
            return (response.StatusCode, await ZipNamesAsync(response));
        }
        catch (Exception e) when (e is not TimeoutException)
        {
            return (response?.StatusCode, null);
        }
    }

    [Fact]
    public async Task Read_taken_away_while_a_zip_is_made_applies_to_what_is_not_in_it_yet()
    {
        _ = Tree.File("docs/z-last.md", "after the pause");
        (Task reached, Action release) = PauseZipAt("docs/nested");
        Task<HttpResponseMessage> download = Client.GetAsync(Users.Alice, "/docs?download");
        await reached.WaitAsync(Timeout, Cancellation);

        Tree.Config("docs", Cfg.Folder(Cfg.Perms(rwDeny: ["g:team"])));
        release();

        (HttpStatusCode? status, List<string>? names) = await DownloadAsync(download);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(names);
        Assert.Contains("a.md", names);
        Assert.DoesNotContain("z-last.md", names);
    }

    [Fact]
    public async Task A_file_deleted_while_a_zip_is_made_breaks_off_the_download_or_is_left_out_and_the_app_goes_on()
    {
        // The zip has found b.md and is about to put it in
        (Task reached, Action release) = PauseZipAt("docs/nested/b.md");
        Task<HttpResponseMessage> download = Client.GetAsync(Users.Alice, "/docs?download");
        await reached.WaitAsync(Timeout, Cancellation);

        File.Delete(Tree.Physical("docs/nested/b.md"));
        release();

        (_, List<string>? names) = await DownloadAsync(download);
        if (names is not null)
            Assert.DoesNotContain("nested/b.md", names);
        App.BeforeAccessCheck = null;
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs"));
    }

    [Fact]
    public async Task Stale_uploads_cleared_while_a_zip_of_the_root_is_made_are_not_part_of_it()
    {
        string id = await StartUploadAsync(Users.Alice, "docs");
        _ = await ChunkAsync(Users.Alice, "docs", id, "half.md", 0, false, "half");
        (Task reached, Action release) = PauseZipAt("docs");
        Task<HttpResponseMessage> download = Client.GetAsync(Users.Alice, "/?download");
        await reached.WaitAsync(Timeout, Cancellation);

        App.Time.Advance(TimeSpan.FromHours(2));
        RunStaleUploadsJob();
        release();

        (HttpStatusCode? status, List<string>? names) = await DownloadAsync(download);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(names);
        Assert.DoesNotContain(names, name => name.StartsWith("_", StringComparison.Ordinal) || name.EndsWith(".part", StringComparison.Ordinal));
    }
}
