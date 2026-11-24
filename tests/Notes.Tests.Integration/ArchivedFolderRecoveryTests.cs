using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Components.Archive;
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
        Assert.Empty(Directory.EnumerateFileSystemEntries(Tree.Physical("_/temp/zipping/unzipping")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Tree.Physical("_/temp/zipping/deleting")));
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
    public async Task A_folder_whose_archiving_was_turned_on_but_not_done_yet_is_made_one()
    {
        Tree.File("docs/vault/note.md", "inside");
        Tree.Config("docs/vault", ArchivedConfig());
        await StartAsync();

        Assert.Equal("inside", await ContentsOf("/docs/vault/note.md"));
        Assert.Equal([".config.json"], Directory.EnumerateFileSystemEntries(Outer).Select(Path.GetFileName));
    }
}
