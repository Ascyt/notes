using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Notes.Features.Components.Archive;

namespace Notes.Tests.Integration;

/// <summary>
/// An archive is made before it is downloaded, which may take long: the permissions are checked again once it is written, and it is deleted an
/// hour later (principle 13, security specification: "What only reads checks again at its end").
/// </summary>
[Trait("Category", "Timing")]
public sealed class ArchiveTimingTests() : ArchiveTest(new NotesAppFactory()), IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async ValueTask DisposeAsync()
    {
        App.BeforeAccessCheck = null;
        await App.DisposeAsync();
    }

    /// <summary>Holds the making of alice's archive when it asks about <paramref name="path"/>, until the returned release is called.</summary>
    private (Task Reached, Action Release) PauseAt(string path)
    {
        string physical = Tree.Physical(path);
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ManualResetEventSlim release = new();
        App.BeforeAccessCheck = (user, asked) =>
        {
            if (user.FindFirst("sub")?.Value == Users.Alice.Sub && string.Equals(asked, physical, StringComparison.OrdinalIgnoreCase)
                && reached.TrySetResult())
                _ = release.Wait(Timeout);
        };
        return (reached.Task, release.Set);
    }

    [Fact]
    public async Task Read_taken_away_from_what_is_already_in_the_archive_deletes_it_instead_of_handing_it_out()
    {
        // a.md is in the archive by the time it gets to nested
        (Task reached, Action release) = PauseAt("docs/nested");
        Task<HttpResponseMessage> prepared = PrepareAsync(Users.Alice, "/docs", "directory");
        await reached.WaitAsync(Timeout, Cancellation);

        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("a.md", Cfg.Perms(inherit: false))));
        release();

        await AssertStatus(HttpStatusCode.Conflict, await prepared.WaitAsync(Timeout, Cancellation));
        Assert.Empty(ArchiveFiles());
    }

    [Fact]
    public async Task Read_taken_away_from_a_selected_entry_while_it_is_archived_deletes_it_too()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md", "team");
        (Task reached, Action release) = PauseAt("team/sub");
        Task<HttpResponseMessage> prepared = PrepareAsync(Users.Alice, "/", "selection", version: selection.Version);
        await reached.WaitAsync(Timeout, Cancellation);

        Tree.Config("docs", Cfg.Folder(Cfg.Perms(inherit: false)));
        release();

        await AssertStatus(HttpStatusCode.Conflict, await prepared.WaitAsync(Timeout, Cancellation));
        Assert.Empty(ArchiveFiles());
    }

    [Fact]
    public async Task An_archive_is_deleted_an_hour_after_it_was_made()
    {
        HttpResponseMessage prepared = await PrepareAsync(Users.Alice, "/docs", "directory");
        await AssertStatus(HttpStatusCode.OK, prepared);
        Guid id = (await prepared.Content.ReadFromJsonAsync<PreparedArchiveDto>(Cancellation))!.Id;

        App.Time.Advance(TimeSpan.FromMinutes(59));
        RunStaleArchivesJob();
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs?archive=" + id));

        App.Time.Advance(TimeSpan.FromMinutes(2));
        RunStaleArchivesJob();
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs?archive=" + id));
        Assert.Empty(ArchiveFiles());
    }

    [Fact]
    public async Task Files_no_archive_is_known_for_are_deleted_once_they_are_an_hour_old()
    {
        // As a restart leaves them: on disk, but nobody's
        string orphan = Tree.File("_/temp/archive/left-behind.7z", "x");
        File.SetLastWriteTimeUtc(orphan, App.Time.GetUtcNow().UtcDateTime);

        App.Time.Advance(TimeSpan.FromMinutes(61));
        RunStaleArchivesJob();

        Assert.False(File.Exists(orphan));
    }

    private Task<HttpResponseMessage> PrepareAtOnceAsync(TestUser user, string address)
        => Client.SendAsync(user, HttpMethod.Post, address + "?archive=directory",
            JsonContent.Create(new { format = "7z", compression = "high" }), headers: new Dictionary<string, string> { ["Prefer"] = "respond-async" });

    private sealed record ProgressDto(bool Done, double Progress, bool LeftOutUnreadable);

    private async Task<HttpResponseMessage> ProgressAsync(TestUser user, Guid id)
        => await Client.GetAsync(user, "/docs?archive=" + id + "&progress");

    [Fact]
    public async Task Asked_to_answer_at_once_it_is_made_meanwhile_and_says_how_far_it_got()
    {
        (Task reached, Action release) = PauseAt("docs/nested");
        HttpResponseMessage started = await PrepareAtOnceAsync(Users.Alice, "/docs");
        await AssertStatus(HttpStatusCode.Accepted, started);
        Guid id = (await started.Content.ReadFromJsonAsync<PreparedArchiveDto>(Cancellation))!.Id;
        await reached.WaitAsync(Timeout, Cancellation);

        ProgressDto running = (await (await ProgressAsync(Users.Alice, id)).Content.ReadFromJsonAsync<ProgressDto>(Cancellation))!;
        Assert.False(running.Done);
        Assert.InRange(running.Progress, 0, 0.999);
        // Not to be downloaded yet, and nobody else's to ask about
        await AssertStatus(HttpStatusCode.Conflict, await Client.GetAsync(Users.Alice, "/docs?archive=" + id));
        await AssertStatus(HttpStatusCode.NotFound, await ProgressAsync(Users.Carol, id));
        release();

        ProgressDto done = await WaitUntilDoneAsync(id);
        Assert.Equal(1, done.Progress);
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs?archive=" + id));
    }

    [Fact]
    public async Task One_nobody_asks_about_for_a_minute_is_cancelled_and_nothing_is_kept()
    {
        (Task reached, Action release) = PauseAt("docs/nested");
        HttpResponseMessage started = await PrepareAtOnceAsync(Users.Alice, "/docs");
        Guid id = (await started.Content.ReadFromJsonAsync<PreparedArchiveDto>(Cancellation))!.Id;
        await reached.WaitAsync(Timeout, Cancellation);

        App.Time.Advance(PreparedArchives.AbandonAfter + TimeSpan.FromSeconds(1));
        release();

        // Waited for without asking (which would count as the page still waiting)
        ArchiveJob job = App.Services.GetRequiredService<PreparedArchives>().FindJob(Users.Alice.Sub!, id)!;
        ArchiveJobResult result = await job.Finished.WaitAsync(Timeout, Cancellation);
        Assert.Null(result.Archive);
        await AssertStatus(HttpStatusCode.Gone, await ProgressAsync(Users.Alice, id));
        Assert.Empty(ArchiveFiles());
        // Another one can be made right away
        await AssertStatus(HttpStatusCode.OK, await PrepareAsync(Users.Alice, "/docs", "directory"));
    }

    [Fact]
    public async Task A_refusal_at_the_end_is_what_asking_about_it_answers()
    {
        (Task reached, Action release) = PauseAt("docs/nested");
        HttpResponseMessage started = await PrepareAtOnceAsync(Users.Alice, "/docs");
        Guid id = (await started.Content.ReadFromJsonAsync<PreparedArchiveDto>(Cancellation))!.Id;
        await reached.WaitAsync(Timeout, Cancellation);

        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("a.md", Cfg.Perms(inherit: false))));
        release();

        await AssertStatus(HttpStatusCode.Conflict, await WaitUntilFinishedAsync(id));
        Assert.Empty(ArchiveFiles());
    }

    /// <summary>Asks about it (as the page does) until it is no longer being made; the last answer.</summary>
    private async Task<HttpResponseMessage> WaitUntilFinishedAsync(Guid id)
    {
        for (int i = 0; i < 200; i++)
        {
            HttpResponseMessage response = await ProgressAsync(Users.Alice, id);
            if (!response.IsSuccessStatusCode || (await response.Content.ReadFromJsonAsync<ProgressDto>(Cancellation))!.Done)
                return response;
            await Task.Delay(50, Cancellation);
        }
        throw new TimeoutException("The archive was not finished.");
    }

    private async Task<ProgressDto> WaitUntilDoneAsync(Guid id)
    {
        HttpResponseMessage response = await WaitUntilFinishedAsync(id);
        await AssertStatus(HttpStatusCode.OK, response);
        return (await (await ProgressAsync(Users.Alice, id)).Content.ReadFromJsonAsync<ProgressDto>(Cancellation))!;
    }

    private void RunStaleArchivesJob()
    {
        using IServiceScope scope = App.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClearStaleArchivesJob>().Run();
    }
}
