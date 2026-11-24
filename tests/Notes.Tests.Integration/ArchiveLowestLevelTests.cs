using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Notes.Features.Passwords;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Integration;

/// <summary>
/// <c>"onLowestLevel": true</c>: only the folders below that hold files are archived, with the settings of the folder that says it.
/// <code>
/// docs/media/          onLowestLevel (saved by the tests)
///   2024/a/x.md        a holds files: archived
///   2024/b/y.md        b too (2024 holds only folders: not)
///   2025/c/z.md        c holds a file and a folder: archived, with sub in it
///   2025/c/sub/w.md
/// </code>
/// Each test has an app of its own, as which folders are unzipped is kept by the app.
/// </summary>
public sealed class ArchiveLowestLevelTests() : IntegrationTest(new NotesAppFactory()), IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();

    private static string WithArchive(JsonObject? archive)
    {
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        config["archive"] = archive;
        return config.ToJsonString();
    }

    private static string LowestLevel => WithArchive(new JsonObject { ["enabled"] = true, ["onLowestLevel"] = true });

    private void MakeMedia()
    {
        Tree.File("docs/media/2024/a/x.md", "x");
        Tree.File("docs/media/2024/b/y.md", "y");
        Tree.File("docs/media/2025/c/z.md", "z");
        Tree.File("docs/media/2025/c/sub/w.md", "w");
    }

    private Task<HttpResponseMessage> SaveAsync(string folder, string config)
        => Client.PostAsync(Users.Admin, "/" + folder + "?config", config);

    private async Task<string> ContentsOf(string address)
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, address + "?download");
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    private ArchiveState? StateOf(string folder) => Archives.ArchiveStateOf(Tree.Physical(folder));

    [Fact]
    public async Task Only_the_folders_that_hold_files_are_archived_and_everything_is_where_it_was()
    {
        MakeMedia();

        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", LowestLevel));

        Assert.Null(StateOf("docs/media"));
        Assert.Null(StateOf("docs/media/2024"));
        Assert.Equal(ArchiveState.Unzipped, StateOf("docs/media/2024/a"));
        Assert.Equal(ArchiveState.Unzipped, StateOf("docs/media/2024/b"));
        Assert.Equal(ArchiveState.Unzipped, StateOf("docs/media/2025/c"));
        // sub is in c's archive, not one of its own
        Assert.Null(Archives.MountedAt(Tree.Physical("docs/media/2025/c/sub")));
        Assert.Equal("w", await ContentsOf("/docs/media/2025/c/sub/w.md"));

        // Zipped like any other, with the settings of media
        Assert.True(await Archives.ZipAsync(Tree.Physical("docs/media/2024/a")));
        Assert.True(File.Exists(Tree.Physical("docs/media/2024/a/_.7z")));
        Assert.Equal("x", await ContentsOf("/docs/media/2024/a/x.md"));
    }

    [Fact]
    public async Task The_configurations_below_leave_it_to_the_folder_above()
    {
        MakeMedia();
        Tree.Config("docs/media/2024", WithArchive(new JsonObject { ["enabled"] = false }));

        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", LowestLevel));

        Assert.Matches(@"""archive"":\s*null", await File.ReadAllTextAsync(Tree.Physical("docs/media/2024/.config.json"), Cancellation));
        Assert.Equal(ArchiveState.Unzipped, StateOf("docs/media/2024/a"));
    }

    [Fact]
    public async Task It_is_refused_over_entries_with_a_password_which_it_would_get_around()
    {
        MakeMedia();
        JsonObject withPassword = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        withPassword["passwordHash"] = PasswordHashing.Hash("secret", 4);
        Tree.Config("docs/media/2024/a", withPassword.ToJsonString());

        HttpResponseMessage response = await SaveAsync("docs/media", LowestLevel);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Contains("docs/media/2024/a", await BodyOf(response));
        Assert.Null(StateOf("docs/media/2024/b"));
    }

    [Fact]
    public async Task A_folder_that_comes_to_hold_files_is_archived_by_the_job()
    {
        MakeMedia();
        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", LowestLevel));
        Tree.File("docs/media/2026/d/q.md", "q");
        Assert.Null(Archives.MountedAt(Tree.Physical("docs/media/2026/d")));

        using (IServiceScope scope = App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ZipIdleArchivesJob>().RunAsync();

        Assert.Equal(ArchiveState.Unzipped, StateOf("docs/media/2026/d"));
        Assert.Equal("q", await ContentsOf("/docs/media/2026/d/q.md"));
    }

    [Fact]
    public async Task A_folder_that_gets_its_first_file_in_the_app_is_archived_right_away()
    {
        MakeMedia();
        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", LowestLevel));
        _ = Directory.CreateDirectory(Tree.Physical("docs/media/2026/d"));

        HttpResponseMessage created = await Client.PostAsync(Users.Alice, "/docs/media/2026/d?create=file");

        Assert.True(created.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.OK, $"Got {created.StatusCode}");
        // No job needed: it is like the others at once, unzipped
        Assert.Equal(ArchiveState.Unzipped, StateOf("docs/media/2026/d"));
        Assert.Null(StateOf("docs/media/2026"));
        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/media/2026"));
        Assert.Contains("data-archived-mark=\"unzipped\"", listing);
    }

    [Fact]
    public async Task Archiving_inside_an_archived_folder_is_saved_with_a_warning()
    {
        MakeMedia();
        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", LowestLevel));

        HttpResponseMessage response = await SaveAsync("docs/media/2025/c/sub", WithArchive(new JsonObject { ["enabled"] = true }));

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.TryGetValues("X-Notes-Config-Warning", out IEnumerable<string>? warnings));
        Assert.Contains("archived folder", Uri.UnescapeDataString(warnings.Single()));
        HttpResponseMessage plain = await SaveAsync("docs/media/2024", WithArchive(null));
        Assert.False(plain.Headers.Contains("X-Notes-Config-Warning"));
    }

    [Fact]
    public async Task The_soft_restart_does_what_a_configuration_edited_on_the_server_says()
    {
        MakeMedia();
        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", LowestLevel));
        // Edited outside the app: onLowestLevel off
        Tree.Config("docs/media", WithArchive(new JsonObject { ["enabled"] = false }));

        using (IServiceScope scope = App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Notes.Features.Maintenance.SoftRestartJob>().RunAsync();

        Assert.Null(StateOf("docs/media/2024/a"));
        Assert.True(File.Exists(Tree.Physical("docs/media/2024/a/x.md")));
    }

    [Fact]
    public async Task Cutting_a_folder_off_or_turning_it_off_above_makes_them_ordinary_folders_again()
    {
        MakeMedia();
        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", LowestLevel));
        Assert.True(await Archives.ZipAsync(Tree.Physical("docs/media/2025/c")));

        // Cut off below
        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media/2025", WithArchive(new JsonObject { ["enabled"] = false })));
        Assert.Null(StateOf("docs/media/2025/c"));
        Assert.True(File.Exists(Tree.Physical("docs/media/2025/c/z.md")));
        Assert.Equal(ArchiveState.Unzipped, StateOf("docs/media/2024/a"));

        // Off above
        await AssertStatus(HttpStatusCode.OK, await SaveAsync("docs/media", WithArchive(new JsonObject { ["enabled"] = false })));
        Assert.Null(StateOf("docs/media/2024/a"));
        Assert.Null(StateOf("docs/media/2024/b"));
        Assert.True(File.Exists(Tree.Physical("docs/media/2024/a/x.md")));
        Assert.Equal("w", await ContentsOf("/docs/media/2025/c/sub/w.md"));
    }
}
