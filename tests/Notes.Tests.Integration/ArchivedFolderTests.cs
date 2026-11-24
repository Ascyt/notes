using System.Net;
using System.Net.Http.Json;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using Notes.Features.History;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Integration;

/// <summary>
/// Archived folders (<c>"archive": { "enabled": true }</c>): turning it on, using what is in them (unzipped on the way), zipping them again
/// once they aren't used, and their history. Each test has an app of its own, as which folders are unzipped is kept by the app.
/// </summary>
public sealed class ArchivedFolderTests() : IntegrationTest(new NotesAppFactory()), IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();

    private string Outer => Tree.Physical("docs/vault");

    /// <summary>docs/vault with two files, made an archived folder by an admin saving its configuration.</summary>
    private async Task ArchiveVaultAsync()
    {
        Tree.File("docs/vault/note.md", "inside");
        Tree.File("docs/vault/deeper/more.md", "more");
        string config = $$"""
                          {
                              "version": "{{Cfg.Version}}",
                              "perms": { "inherit": true },
                              "gitignore": false,
                              "archive": { "enabled": true, "filetype": "7z", "compressionLevel": 1 },
                              "files": []
                          }
                          """;
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs/vault?config", config));
    }

    private void AdvancePastIdle()
        => App.Time.Advance(TimeSpan.FromMinutes(App.Services.GetRequiredService<Config>().Archives.ZipAfterMinutes) + TimeSpan.FromSeconds(1));

    private async Task RunZipJobAsync()
    {
        using IServiceScope scope = App.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ZipIdleArchivesJob>().RunAsync();
    }

    /// <summary>What the file holds, as a download reads it.</summary>
    private async Task<string> ContentsOf(TestUser user, string address)
    {
        HttpResponseMessage response = await Client.GetAsync(user, address + "?download");
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    /// <summary>The configuration of an archived folder with the permissions.</summary>
    private static string ArchivedWith(System.Text.Json.Nodes.JsonObject perms)
    {
        System.Text.Json.Nodes.JsonObject config = System.Text.Json.Nodes.JsonNode.Parse(Cfg.Folder(perms))!.AsObject();
        config["archive"] = new System.Text.Json.Nodes.JsonObject { ["enabled"] = true };
        return config.ToJsonString();
    }

    private string[] OnDiskInOuter() => [.. Directory.EnumerateFileSystemEntries(Outer).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [Fact]
    public async Task Turning_it_on_keeps_everything_where_it_was_for_the_users()
    {
        await ArchiveVaultAsync();

        // On disk the folder only has its configuration now (what was in it is its unzipped copy, mounted in its place)
        Assert.Equal([".config.json"], OnDiskInOuter());
        Assert.NotNull(Archives.MountedAt(Outer));
        Assert.Equal("inside", await ContentsOf(Users.Alice, "/docs/vault/note.md"));
        Assert.Equal("more", await ContentsOf(Users.Alice, "/docs/vault/deeper/more.md"));
        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/vault"));
        Assert.Contains("note.md", listing);
    }

    [Fact]
    public async Task Zipped_it_holds_only_its_configuration_and_archive_and_is_unzipped_by_the_next_request()
    {
        await ArchiveVaultAsync();

        Assert.True(await Archives.ZipAsync(Outer));

        Assert.Equal([".config.json", "_.7z"], OnDiskInOuter());
        Assert.True(Archives.IsZipped(Outer));
        Assert.Equal("more", await ContentsOf(Users.Alice, "/docs/vault/deeper/more.md"));
        Assert.NotNull(Archives.MountedAt(Outer));
    }

    [Fact]
    public async Task What_was_saved_while_unzipped_is_in_the_archive_after_zipping()
    {
        await ArchiveVaultAsync();
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/vault/note.md", "changed"));

        Assert.True(await Archives.ZipAsync(Outer));
        Assert.True(Archives.IsZipped(Outer));
        Assert.Equal("changed", await ContentsOf(Users.Alice, "/docs/vault/note.md"));
    }

    [Fact]
    public async Task It_is_zipped_again_once_it_was_not_used_for_a_while()
    {
        await ArchiveVaultAsync();

        await RunZipJobAsync();
        Assert.NotNull(Archives.MountedAt(Outer));

        AdvancePastIdle();
        await RunZipJobAsync();
        Assert.True(Archives.IsZipped(Outer));
    }

    [Fact]
    public async Task Using_it_keeps_it_unzipped()
    {
        await ArchiveVaultAsync();
        AdvancePastIdle();
        _ = await Client.GetAsync(Users.Alice, "/docs/vault/note.md");

        await RunZipJobAsync();

        Assert.NotNull(Archives.MountedAt(Outer));
    }

    [Fact]
    public async Task Who_may_not_open_it_does_not_get_it_unzipped()
    {
        await ArchiveVaultAsync();
        Tree.Config("docs/vault", ArchivedWith(Cfg.Perms(inherit: false, rwAllow: ["u:alice"])));
        Assert.True(await Archives.ZipAsync(Outer));

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Carol, "/docs/vault/note.md"));

        Assert.True(Archives.IsZipped(Outer));
    }

    [Fact]
    public async Task Asked_how_far_it_got_it_is_unzipped_and_says_so()
    {
        await ArchiveVaultAsync();
        Assert.True(await Archives.ZipAsync(Outer));

        string answer = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/vault?unzip"));

        Assert.Contains("\"done\":true", answer);
        Assert.NotNull(Archives.MountedAt(Outer));
    }

    [Fact]
    public async Task Its_configuration_can_be_edited_while_it_is_zipped_without_unzipping_it()
    {
        await ArchiveVaultAsync();
        Assert.True(await Archives.ZipAsync(Outer));

        string editor = await BodyOf(await Client.GetAsync(Users.Admin, "/docs/vault?config"));

        Assert.Contains("compressionLevel", editor);
        Assert.True(Archives.IsZipped(Outer));
    }

    [Fact]
    public async Task Turning_it_off_is_not_supported_yet()
    {
        await ArchiveVaultAsync();

        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/docs/vault?config", Cfg.Folder(Cfg.Perms()));

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.NotNull(Archives.MountedAt(Outer));
    }

    [Fact]
    public async Task The_outer_history_keeps_its_configuration_but_not_its_archive_and_it_has_a_history_of_its_own()
    {
        await ArchiveVaultAsync();
        HistoryService history = App.Services.GetRequiredService<HistoryService>();
        history.CommitPending(CommitReason.Startup);
        Assert.True(await Archives.ZipAsync(Outer));
        history.CommitPending(CommitReason.Startup);

        using (Repository outer = new(history.GitDirectory))
        {
            Commit latest = outer.Head.Tip;
            Assert.NotNull(latest["docs/vault/.config.json"]);
            Assert.Null(latest["docs/vault/_.7z"]);
            Assert.Null(latest["docs/vault/note.md"]);
        }

        // Its own history is in the archive: unzipped again, it has what was committed before it was zipped
        _ = await Client.GetAsync(Users.Alice, "/docs/vault/note.md");
        EntryLocationGitDirectory(out string gitDirectory);
        using Repository own = new(gitDirectory);
        Assert.NotNull(own.Head.Tip["note.md"]);
    }

    [Fact]
    public async Task Renaming_it_while_it_is_unzipped_zips_it_first_and_it_moves_with_its_archive()
    {
        await ArchiveVaultAsync();

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/vault?rename=renamed", ""));

        Assert.False(Directory.Exists(Outer));
        Assert.True(File.Exists(Tree.Physical("docs/renamed/_.7z")));
        Assert.Equal("inside", await ContentsOf(Users.Alice, "/docs/renamed/note.md"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deleting_it_takes_its_folder_and_archive_away(bool zippedFirst)
    {
        await ArchiveVaultAsync();
        if (zippedFirst)
            Assert.True(await Archives.ZipAsync(Outer));

        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Admin, "/docs/vault"));

        Assert.False(Directory.Exists(Outer));
        Assert.Null(Archives.MountedAt(Outer));
    }

    [Fact]
    public async Task Deleting_what_holds_it_takes_it_with_it()
    {
        await ArchiveVaultAsync();

        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Admin, "/docs"));

        Assert.False(Directory.Exists(Tree.Physical("docs")));
        Assert.Empty(Tree.Physical("_/temp/unzipped") is var copies && Directory.Exists(copies)
            ? Directory.EnumerateDirectories(copies, "*", SearchOption.AllDirectories).Where(path => path.EndsWith("vault", StringComparison.Ordinal))
            : []);
    }

    [Fact]
    public async Task What_is_in_it_that_the_user_may_not_read_keeps_them_from_deleting_it_also_while_it_is_zipped()
    {
        await ArchiveVaultAsync();
        // (In its unzipped copy, which is what the folder holds now)
        await File.WriteAllTextAsync(Path.Combine(Archives.MountedAt(Outer)!.PhysicalPath, "deeper", ".config.json"),
            Cfg.Folder(Cfg.Perms(inherit: false)), Cancellation);
        Assert.True(await Archives.ZipAsync(Outer));

        HttpResponseMessage response = await Client.DeleteAsync(Users.Alice, "/docs/vault");

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("inside", await ContentsOf(Users.Alice, "/docs/vault/note.md"));
    }

    [Fact]
    public async Task Moved_it_stays_archived_with_everything_in_it()
    {
        await ArchiveVaultAsync();

        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Admin, "move", "team", "docs/vault"));

        Assert.False(Directory.Exists(Outer));
        Assert.True(File.Exists(Tree.Physical("team/vault/_.7z")));
        Assert.Equal("more", await ContentsOf(Users.Admin, "/team/vault/deeper/more.md"));
        Assert.True(Archives.IsArchived(Tree.Physical("team/vault")));
    }

    [Fact]
    public async Task Moved_by_a_user_who_does_not_manage_configurations_it_still_goes_with_its_configuration_and_archive()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(rwAllow: ["u:alice"], cdAllow: ["u:alice"])));
        Tree.Config("team", Cfg.Folder(Cfg.Perms(rwAllow: ["u:alice"], cdAllow: ["u:alice"])));
        await ArchiveVaultAsync();

        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Alice, "move", "team", "docs/vault"));

        Assert.True(Archives.IsArchived(Tree.Physical("team/vault")));
        Assert.Equal("inside", await ContentsOf(Users.Alice, "/team/vault/note.md"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/team/vault/_.7z?download"));
    }

    [Fact]
    public async Task A_copy_is_a_normal_folder_with_what_is_in_it_and_the_original_stays()
    {
        await ArchiveVaultAsync();
        Assert.True(await Archives.ZipAsync(Outer));

        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Admin, "copy", "team", "docs/vault"));

        Assert.Equal("more", Tree.Read("team/vault/deeper/more.md"));
        Assert.False(File.Exists(Tree.Physical("team/vault/_.7z")));
        Assert.Equal("inside", await ContentsOf(Users.Alice, "/docs/vault/note.md"));
    }

    [Fact]
    public async Task Renaming_what_holds_a_zipped_one_checks_what_is_in_it_and_it_moves_along()
    {
        await ArchiveVaultAsync();
        Assert.True(await Archives.ZipAsync(Outer));

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs?rename=papers2", ""));

        Assert.True(File.Exists(Tree.Physical("papers2/vault/_.7z")));
        Assert.Equal("inside", await ContentsOf(Users.Admin, "/papers2/vault/note.md"));
    }

    [Fact]
    public async Task Its_archive_is_never_handed_out_as_a_file_also_not_to_admins_in_a_download_of_what_holds_it()
    {
        await ArchiveVaultAsync();
        Assert.True(await Archives.ZipAsync(Outer));

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/docs/vault/_.7z?download"));
        foreach (TestUser user in new[] { Users.Admin, Users.Alice })
        {
            HttpResponseMessage response = await Client.GetAsync(user, "/docs?download");
            await AssertStatus(HttpStatusCode.OK, response);
            using System.IO.Compression.ZipArchive zip = new(await response.Content.ReadAsStreamAsync(Cancellation));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.EndsWith("_.7z", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Downloading_it_unzips_it_and_gives_what_is_in_it()
    {
        await ArchiveVaultAsync();
        Assert.True(await Archives.ZipAsync(Outer));

        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/docs/vault?download");

        await AssertStatus(HttpStatusCode.OK, response);
        using System.IO.Compression.ZipArchive zip = new(await response.Content.ReadAsStreamAsync(Cancellation));
        Assert.Contains(zip.Entries, entry => entry.FullName == "note.md");
    }

    private sealed record PreparedDto(Guid Id);

    private sealed record ZippedDto(string? Location);

    /// <summary>Waits for what runs apart from the request (the archive button zips in the background).</summary>
    private static async Task Eventually(Func<bool> condition, string what)
    {
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                Assert.Fail("Timed out waiting until " + what);
            await Task.Delay(20, Cancellation);
        }
    }

    [Fact]
    public async Task An_admin_downloads_it_as_it_is_kept_zipped_first_and_named_after_it()
    {
        await ArchiveVaultAsync();
        Assert.NotNull(Archives.MountedAt(Outer));

        HttpResponseMessage prepared = await Client.PostJsonAsync(Users.Admin, "/docs/vault?archive=as-is", new { });
        await AssertStatus(HttpStatusCode.OK, prepared);
        Guid id = (await prepared.Content.ReadFromJsonAsync<PreparedDto>(Cancellation))!.Id;

        // Zipped for it: the download is the archive the folder is kept in now
        Assert.True(Archives.IsZipped(Outer));
        HttpResponseMessage download = await Client.GetAsync(Users.Admin, "/docs/vault?archive=" + id);
        await AssertStatus(HttpStatusCode.OK, download);
        Assert.Equal("vault.7z", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(Outer, "_.7z"), Cancellation),
            await download.Content.ReadAsByteArrayAsync(Cancellation));
    }

    [Fact]
    public async Task Only_admins_download_it_as_it_is_kept_and_only_an_archived_one()
    {
        await ArchiveVaultAsync();
        Tree.File("docs/plain/a.md", "a");

        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostJsonAsync(Users.Alice, "/docs/vault?archive=as-is", new { }));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Admin, "/docs/plain?archive=as-is", new { }));
    }

    [Fact]
    public async Task The_archive_button_zips_what_is_unzipped_and_sends_the_page_out_of_it()
    {
        await ArchiveVaultAsync();
        Assert.NotNull(Archives.MountedAt(Outer));

        HttpResponseMessage response = await Client.PostAsync(Users.Alice, "/docs/vault/deeper?zip");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("/docs", (await response.Content.ReadFromJsonAsync<ZippedDto>(Cancellation))!.Location);
        await Eventually(() => Archives.IsZipped(Outer) && Archives.ZipProgressOf(Outer) is null, "it is zipped");
    }

    [Fact]
    public async Task The_archive_button_is_shown_while_something_is_unzipped_and_the_listing_marks_how_it_is_kept()
    {
        await ArchiveVaultAsync();

        string unzipped = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Contains("data-archived-mark=\"unzipped\"", unzipped);
        Assert.Contains("<li class=\"nav-item\" data-zip-item>", unzipped);

        Assert.True(await Archives.ZipAsync(Outer));
        string zipped = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Contains("data-archived-mark=\"zipped\"", zipped);
        Assert.Contains("data-zip-item hidden", zipped);
    }

    // ---- While it is being zipped

    private sealed record ZippingDto(string? State, double Progress);

    /// <summary>
    /// Holds every zipping once its new archive is written (before it is checked), until <c>release</c> is set; <c>reached</c> is set when one
    /// gets there.
    /// </summary>
    private (ManualResetEventSlim Reached, ManualResetEventSlim Release) HoldZippingOnceWritten()
    {
        ManualResetEventSlim reached = new();
        ManualResetEventSlim release = new();
        App.Services.GetRequiredService<FolderArchiver>().StepReached = step =>
        {
            if (step != ArchiveStep.ArchiveWritten)
                return;
            reached.Set();
            _ = release.Wait(TimeSpan.FromSeconds(30));
        };
        return (reached, release);
    }

    private async Task<ZippingDto> ZippingOf(string address)
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, address + "?zipping");
        await AssertStatus(HttpStatusCode.OK, response);
        return (await response.Content.ReadFromJsonAsync<ZippingDto>(Cancellation))!;
    }

    [Fact]
    public async Task While_it_is_zipped_the_listing_shows_how_far_and_asking_does_not_stop_it()
    {
        await ArchiveVaultAsync();
        AdvancePastIdle();
        (ManualResetEventSlim reached, ManualResetEventSlim release) = HoldZippingOnceWritten();

        Task zipping = Task.Run(RunZipJobAsync, Cancellation);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), Cancellation));

        ZippingDto state = await ZippingOf("/docs/vault");
        Assert.Equal("zipping", state.State);
        // Written: 70 % (writing is that part of it)
        Assert.InRange(state.Progress, 0.6, 0.71);
        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Contains("data-archived-mark=\"zipping\"", listing);
        Assert.Contains("data-zipping-address=\"/docs/vault\"", listing);

        release.Set();
        await zipping;

        Assert.True(Archives.IsZipped(Outer));
        Assert.Equal("zipped", (await ZippingOf("/docs/vault")).State);
    }

    [Fact]
    public async Task Opening_it_while_it_is_zipped_stops_that_and_its_copy_is_used_as_before()
    {
        await ArchiveVaultAsync();
        AdvancePastIdle();
        (ManualResetEventSlim reached, ManualResetEventSlim release) = HoldZippingOnceWritten();
        Task zipping = Task.Run(RunZipJobAsync, Cancellation);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), Cancellation));

        // What a request into it does (ArchiveGate): it looks zipped, so it is unzipped, which stops the zipping first
        UnzipJob opening = Archives.Unzip(Outer);
        release.Set();
        await zipping;

        Assert.Null(await opening.Finished);
        Assert.NotNull(Archives.MountedAt(Outer));
        // Never zipped: no archive, nor the new one that was being written
        Assert.Equal([".config.json"], OnDiskInOuter());
        Assert.Equal("inside", await ContentsOf(Users.Alice, "/docs/vault/note.md"));
        Assert.Equal("unzipped", (await ZippingOf("/docs/vault")).State);
    }

    [Fact]
    public async Task Zipping_that_a_move_or_delete_waits_for_is_not_stopped_by_opening()
    {
        await ArchiveVaultAsync();
        (ManualResetEventSlim reached, ManualResetEventSlim release) = HoldZippingOnceWritten();
        // (As WhileZippedAsync zips: it may not be stopped)
        Task<bool> zipping = Task.Run(() => Archives.ZipAsync(Outer), Cancellation);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), Cancellation));

        UnzipJob opening = Archives.Unzip(Outer);
        release.Set();

        Assert.True(await zipping);
        // Zipped, then unzipped again for who opened it
        Assert.Null(await opening.Finished);
        Assert.Equal([".config.json", "_.7z"], OnDiskInOuter());
        Assert.NotNull(Archives.MountedAt(Outer));
    }

    [Fact]
    public async Task Only_who_may_read_it_is_told_how_it_is_kept()
    {
        await ArchiveVaultAsync();
        Tree.Config("docs/vault", ArchivedWith(Cfg.Perms(inherit: false, rwAllow: ["u:alice"])));

        Assert.Equal("unzipped", (await ZippingOf("/docs/vault")).State);
        HttpResponseMessage carol = await Client.GetAsync(Users.Carol, "/docs/vault?zipping");
        Assert.True(carol.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"Carol got {carol.StatusCode}");
    }

    [Fact]
    public async Task A_request_while_it_is_zipped_for_the_first_time_finds_what_is_in_it()
    {
        await ArchiveVaultAsync();
        (ManualResetEventSlim reached, ManualResetEventSlim release) = HoldZippingOnceWritten();
        // Not mounted while it is written, and no archive yet: the request must not find the folder empty
        Task<bool> zipping = Task.Run(() => Archives.ZipAsync(Outer), Cancellation);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), Cancellation));

        Task<string> reading = ContentsOf(Users.Alice, "/docs/vault/note.md");
        await Task.Delay(300, Cancellation);
        release.Set();

        Assert.Equal("inside", await reading);
        Assert.True(await zipping);
    }

    private void EntryLocationGitDirectory(out string gitDirectory)
        => gitDirectory = Path.Combine(Archives.MountedAt(Outer)!.DataDirectory, "history");
}
