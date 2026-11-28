using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Components.Archive;
using Notes.Features.Operations;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Integration;

/// <summary>
/// What the app finds of an archived folder when it starts after it stopped (a crash, a power cut): whatever it was doing, nothing is lost.
/// What is on disk is laid out, then what the app does when it starts (<see cref="ArchivedFolders.RecoverAsync"/>) is done (the app of the
/// test is already started by then, with nothing to recover).
/// </summary>
public sealed class ArchivedFolderRecoveryTests() : IntegrationTest(new NotesAppFactory()), IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    private Task StartAsync() => App.Services.GetRequiredService<ArchivedFolders>().RecoverAsync();

    private string Outer => Tree.Physical("docs/vault");
    private string Copy => Tree.Physical("_/temp/unzipped/docs/vault");

    private static string ArchivedConfig()
    {
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        config["archive"] = new JsonObject { ["enabled"] = true };
        return config.ToJsonString();
    }

    /// <summary>docs/vault zipped, holding note.md with the text, as the app leaves it.</summary>
    private async Task ZippedVaultAsync(string text)
    {
        Tree.Config("docs/vault", ArchivedConfig());
        string staging = Tree.Physical("staging-of-the-test");
        _ = Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(Path.Combine(staging, "note.md"), text, Cancellation);
        ArchivedFolder folder = new(Path.Combine(Outer, "_.7z"), staging, Tree.Physical("_/temp/zipping"));
        await new FolderArchiver(NullLogger<FolderArchiver>.Instance).ZipAsync(folder,
            new ArchiveOptions(ArchiveFormat.SevenZip, ArchiveCompression.Min, Password: null), Cancellation);
    }

    private async Task<string> ContentsOf(string address)
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, address + "?download");
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    [Fact]
    public async Task A_copy_that_was_unzipped_counts_and_is_used_as_it_is()
    {
        await ZippedVaultAsync("in the archive");
        Tree.File("_/temp/unzipped/docs/vault/note.md", "changed since");
        await StartAsync();

        Assert.NotNull(App.Services.GetRequiredService<ArchivedFolders>().MountedAt(Outer));
        Assert.Equal("changed since", await ContentsOf("/docs/vault/note.md"));
        Assert.NotNull(App.Services.GetRequiredService<ArchivedFolders>().MountedAt(Outer));
    }

    [Fact]
    public async Task What_zipping_or_unzipping_left_half_done_is_deleted_and_the_archive_counts()
    {
        await ZippedVaultAsync("in the archive");
        Tree.File("docs/vault/_.7z.new", "half written");
        Tree.File("_/temp/zipping/unzipping/0123/note.md", "half unzipped");
        Tree.File("_/temp/zipping/deleting/4567/note.md", "half deleted");
        await StartAsync();

        Assert.Equal("in the archive", await ContentsOf("/docs/vault/note.md"));
        Assert.False(File.Exists(Path.Combine(Outer, "_.7z.new")));
        // Deleted behind the start, which doesn't wait for it
        await Eventually(() => !Directory.EnumerateFileSystemEntries(Tree.Physical("_/temp/zipping/unzipping")).Any()
            && !Directory.EnumerateFileSystemEntries(Tree.Physical("_/temp/zipping/deleting")).Any(), "what was left is deleted");
        Assert.Empty(App.Services.GetRequiredService<StorageOperations>().Running);
    }

    [Fact]
    public void What_zipping_makes_after_the_start_is_not_taken_for_a_leftover()
    {
        Tree.File("_/temp/zipping/deleting/old/note.md", "left");
        List<string> found = App.Services.GetRequiredService<FolderArchiver>().FindLeftovers(Tree.Physical("_/temp/zipping"));
        // (Made after they were found)
        Tree.File("_/temp/zipping/deleting/new/note.md", "of work that runs");
        App.Services.GetRequiredService<FolderArchiver>().DeleteLeftovers(found, progress: null, status: null, Cancellation);

        Assert.False(Directory.Exists(Tree.Physical("_/temp/zipping/deleting/old")));
        Assert.True(File.Exists(Tree.Physical("_/temp/zipping/deleting/new/note.md")));
    }

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
    public async Task A_folder_that_was_being_turned_into_an_archived_one_gets_its_place_and_configuration_back()
    {
        // Renamed into its copy, but the folder wasn't made again yet
        Tree.File("_/temp/unzipped/docs/vault/note.md", "inside");
        await File.WriteAllTextAsync(Path.Combine(Copy, ".config.json"), ArchivedConfig(), Cancellation);
        await StartAsync();

        Assert.Equal("inside", await ContentsOf("/docs/vault/note.md"));
        Assert.True(File.Exists(Path.Combine(Outer, ".config.json")));
        Assert.False(File.Exists(Path.Combine(Copy, ".config.json")));
    }

    [Fact]
    public async Task Turning_it_off_that_stopped_after_the_copy_was_moved_aside_is_finished()
    {
        await ZippedVaultAsync("in the archive");
        // Turned off: the marker written and the copy moved next to it, but nothing after that
        Tree.Config("docs/vault", Cfg.Folder(Cfg.Perms()));
        Tree.File("_/temp/zipping/turning-off/abc/note.md", "in the copy");
        Tree.File("_/temp/zipping/turning-off/abc/_/history/HEAD", "ref: refs/heads/main");
        Tree.File("_/temp/zipping/turning-off/abc.target", "docs/vault");
        await StartAsync();

        Assert.Equal("in the copy", await ContentsOf("/docs/vault/note.md"));
        Assert.Equal([".config.json", "note.md"], Directory.EnumerateFileSystemEntries(Outer).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Tree.Physical("_/temp/zipping/turning-off")));
        // Its own history set aside, not deleted
        Assert.Single(Directory.EnumerateFiles(Tree.Physical("_/archived-history/docs/vault"), "HEAD", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_folder_turned_off_while_the_app_did_not_run_is_unzipped_into_place()
    {
        await ZippedVaultAsync("in the archive");
        Tree.Config("docs/vault", Cfg.Folder(Cfg.Perms()));
        await StartAsync();

        Assert.Equal("in the archive", await ContentsOf("/docs/vault/note.md"));
        Assert.Equal([".config.json", "note.md"], Directory.EnumerateFileSystemEntries(Outer).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_invalid_configuration_never_counts_as_turned_off()
    {
        await ZippedVaultAsync("in the archive");
        Tree.Config("docs/vault", "{ not valid");
        await StartAsync();

        // Left as it is until the configuration is fixed
        Assert.Equal([".config.json", "_.7z"], Directory.EnumerateFileSystemEntries(Outer).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Both_formats_after_a_change_of_filetype_leave_the_newer_while_the_copy_counts()
    {
        await ZippedVaultAsync("in the archive");
        File.SetLastWriteTimeUtc(Path.Combine(Outer, "_.7z"), DateTime.UtcNow.AddMinutes(-5));
        // The new archive in place, the old one not deleted yet, the copy still there
        File.Copy(Path.Combine(Outer, "_.7z"), Path.Combine(Outer, "_.zip"));
        File.SetLastWriteTimeUtc(Path.Combine(Outer, "_.zip"), DateTime.UtcNow);
        Tree.File("_/temp/unzipped/docs/vault/note.md", "in the copy");
        await StartAsync();

        Assert.Equal("in the copy", await ContentsOf("/docs/vault/note.md"));
        Assert.Equal([".config.json", "_.zip"], Directory.EnumerateFileSystemEntries(Outer).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_folder_whose_archiving_was_turned_on_but_not_done_yet_is_made_one()
    {
        Tree.File("docs/vault/note.md", "inside");
        Tree.Config("docs/vault", ArchivedConfig());
        await StartAsync();

        Assert.Equal("inside", await ContentsOf("/docs/vault/note.md"));
        Assert.Equal([".config.json"], Directory.EnumerateFileSystemEntries(Outer).Select(Path.GetFileName));
    }
}
