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

    private void RunStaleArchivesJob()
    {
        using IServiceScope scope = App.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClearStaleArchivesJob>().Run();
    }
}
