using System.Net;

namespace notes.Tests.Integration;

/// <summary>Changing the notes through bridges: creating, saving, uploading, and copies and moves between physical directories (docs/administration/bridges.md).</summary>
public sealed class BridgeChangeTests(BridgedNotesAppFactory app) : BridgeTest(app)
{
    [Fact]
    public async Task Creating_in_a_bridge_puts_the_entry_into_its_physical_directory()
    {
        HttpResponseMessage response = await Client.PostAsync(Users.Alice, "/hdd/test?create=file");

        await AssertStatus(HttpStatusCode.Redirect, response);
        string created = response.Headers.Location!.OriginalString.Split('?')[0];
        Assert.StartsWith("/hdd/test/", created);
        Assert.True(App.Hdd.IsFile(created["/hdd/".Length..]));
    }

    [Fact]
    public async Task Saving_a_bridged_file()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/hdd/test/note.md", "changed"));

        Assert.Equal("changed", App.Hdd.Read("test/note.md"));
    }

    [Fact]
    public async Task Uploading_into_a_bridge()
    {
        HttpResponseMessage started = await Client.PostAsync(Users.Alice, "/hdd?upload=start");
        await AssertStatus(HttpStatusCode.OK, started);
        string id = await BodyOf(started);

        HttpResponseMessage last = await Client.PostBytesAsync(Users.Alice, $"/hdd?upload={id}&name=up.md&offset=0&last", "uploaded"u8.ToArray());

        await AssertStatus(HttpStatusCode.OK, last);
        Assert.Equal("hdd/up.md", await BodyOf(last));
        Assert.Equal("uploaded", App.Hdd.Read("up.md"));
        Assert.False(App.Tree.Exists("hdd/up.md"));
        // Nothing is left over in the app's data of the bridge
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(App.HddDir, "_", "temp"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Copying_from_the_root_into_a_bridge()
    {
        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Alice, "copy", "hdd/archive", "readme.md"));

        Assert.Equal("# Readme", App.Archive.Read("readme.md"));
        Assert.True(App.Tree.IsFile("readme.md"));
    }

    [Fact]
    public async Task Moving_a_directory_out_of_a_bridge_into_the_root()
    {
        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Alice, "move", "", "hdd/test"));

        Assert.Equal("note", App.Tree.Read("test/note.md"));
        Assert.False(App.Hdd.Exists("test"));
        // Nothing of the transfer is left in the app's data of either
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(App.HddDir, "_", "temp", "transfer")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(App.NotesDir, "_", "temp", "transfer")));
    }

    [Fact]
    public async Task Moving_between_two_bridges()
    {
        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Alice, "move", "hdd/archive", "hdd/test/note.md"));

        Assert.Equal("old", App.Archive.Read("old.md"));
        Assert.Equal("note", App.Archive.Read("note.md"));
        Assert.False(App.Hdd.Exists("test/note.md"));
    }

    [Fact]
    public async Task A_copy_of_a_directory_takes_the_bridges_inside_it_along_as_directories()
    {
        _ = App.Tree.Dir("copies");
        await AssertStatus(HttpStatusCode.OK, await TransferAsync(Users.Admin, "copy", "copies", "hdd"));
        Assert.Equal("note", App.Tree.Read("copies/hdd/test/note.md"));
        Assert.Equal("old", App.Tree.Read("copies/hdd/archive/old.md"));
        Assert.False(App.Tree.Exists("copies/hdd/shadowed.md"));
    }

    [Fact]
    public async Task Nothing_goes_into_itself_across_a_bridge()
        => await AssertStatus(HttpStatusCode.BadRequest, await TransferAsync(Users.Admin, "copy", "hdd/archive", "hdd"));

    [Fact]
    public async Task The_name_of_a_bridge_is_taken_in_its_directory()
    {
        _ = App.Tree.File("other.md", "other");
        string before = string.Join(',', App.Tree.List());

        await AssertStatus(HttpStatusCode.Conflict, await Client.PostAsync(Users.Alice, "/other.md?rename=hdd"));
        await AssertStatus(HttpStatusCode.Conflict, await Client.PostAsync(Users.Alice, "/hdd/test?rename=archive"));
        Assert.Equal(before, string.Join(',', App.Tree.List()));
    }

    [Theory]
    [InlineData("hdd")]
    [InlineData("hdd/archive")]
    public async Task A_bridge_can_not_be_deleted_moved_or_renamed(string bridge)
    {
        HttpResponseMessage deleted = await Client.DeleteAsync(Users.Admin, "/" + bridge);
        HttpResponseMessage renamed = await Client.PostAsync(Users.Admin, "/" + bridge + "?rename=elsewhere");
        _ = App.Tree.Dir("target");
        HttpResponseMessage moved = await TransferAsync(Users.Admin, "move", "target", bridge);

        await AssertStatus(HttpStatusCode.Forbidden, deleted);
        await AssertStatus(HttpStatusCode.Forbidden, renamed);
        await AssertStatus(HttpStatusCode.Forbidden, moved);
        // Admins are told why
        Assert.Contains("bridge", await BodyOf(deleted), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old", App.Archive.Read("old.md"));
        Assert.Equal("note", App.Hdd.Read("test/note.md"));
    }

    [Fact]
    public async Task Nobody_else_learns_why_a_directory_with_a_bridge_can_not_be_deleted()
    {
        HttpResponseMessage alice = await Client.DeleteAsync(Users.Alice, "/hdd");
        // carol may delete all of vault, whose bridge she may read
        HttpResponseMessage carol = await Client.DeleteAsync(Users.Carol, "/vault");

        await AssertStatus(HttpStatusCode.Forbidden, alice);
        await AssertStatus(HttpStatusCode.Forbidden, carol);
        foreach (string message in new[] { await BodyOf(alice), await BodyOf(carol) })
        {
            Assert.DoesNotContain("bridge", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("deep", message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal("the secret", App.Deep.Read("secret.md"));
    }

    [Fact]
    public async Task A_bridge_inside_an_unreadable_directory_is_never_named()
    {
        // alice may not read vault, so deleting it fails because of vault itself
        HttpResponseMessage response = await Client.DeleteAsync(Users.Alice, "/vault");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.DoesNotContain("deep", await BodyOf(response));
    }
}
