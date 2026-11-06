using System.Net;

namespace notes.Tests.Integration;

/// <summary>Renaming and deleting (security specification, Operations).</summary>
public sealed class RenameDeleteTests(NotesAppFactory app) : IntegrationTest(app)
{
    private Task<HttpResponseMessage> RenameAsync(TestUser user, string path, string newName)
        => Client.PostAsync(user, "/" + path + "?rename=" + Uri.EscapeDataString(newName));

    // ---- Rename

    [Fact]
    public async Task Renaming_a_file_and_a_directory()
    {
        HttpResponseMessage file = await RenameAsync(Users.Alice, "docs/a.md", "z.md");
        HttpResponseMessage directory = await RenameAsync(Users.Alice, "docs/nested", "deeper");

        await AssertStatus(HttpStatusCode.OK, file);
        Assert.Equal("/docs/z.md", await BodyOf(file));
        Assert.Equal("/docs/deeper", await BodyOf(directory));
        Assert.True(Tree.IsFile("docs/z.md"));
        Assert.True(Tree.IsFile("docs/deeper/b.md"));
    }

    [Fact]
    public async Task Changing_only_the_case_is_allowed()
    {
        await AssertStatus(HttpStatusCode.OK, await RenameAsync(Users.Alice, "docs/a.md", "A.md"));
        Assert.Equal(["A.md"], Directory.GetFiles(Tree.Physical("docs")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task The_root_can_not_be_renamed()
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Admin, "/?rename=x"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" z.md")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("_")]
    [InlineData(".config.json")]
    [InlineData(".Config.JSON")]
    [InlineData("../z.md")]
    [InlineData("..\\z.md")]
    [InlineData("nested/z.md")]
    [InlineData("nested\\z.md")]
    [InlineData("z.md.")]
    [InlineData(".config.json.")]
    public async Task Invalid_names_are_refused_even_for_admins(string name)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await RenameAsync(Users.Admin, "docs/a.md", name));
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task Taken_names_conflict()
    {
        Tree.File("docs/taken.md", "taken");

        await AssertStatus(HttpStatusCode.Conflict, await RenameAsync(Users.Alice, "docs/a.md", "taken.md"));
        await AssertStatus(HttpStatusCode.Conflict, await RenameAsync(Users.Alice, "docs/a.md", "TAKEN.MD"));
        await AssertStatus(HttpStatusCode.Conflict, await RenameAsync(Users.Alice, "docs/a.md", "nested"));
        Assert.Equal("taken", Tree.Read("docs/taken.md"));
    }

    [Theory]
    [InlineData("shelf/item.md")]     // may not be deleted (and so not renamed)
    [InlineData("drop/existing.md")]  // may only be created
    [InlineData("mixed/keep.md")]     // file entry: may not be deleted
    [InlineData("mixed/locked.md")]   // may not be read
    [InlineData("vault")]             // may not be read
    [InlineData("drop")]
    public async Task Renaming_needs_read_and_delete(string path)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.Forbidden, await RenameAsync(Users.Alice, path, "renamed"));
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task Write_is_not_needed_to_rename()
        => await AssertStatus(HttpStatusCode.OK, await RenameAsync(Users.Alice, "mixed/view.md", "seen.md"));

    [Fact]
    public async Task Bob_and_anonymous_users_can_not_rename()
    {
        await AssertStatus(HttpStatusCode.Forbidden, await RenameAsync(Users.Bob, "docs/a.md", "z.md"));
        await AssertStatus(HttpStatusCode.Forbidden, await RenameAsync(Users.Anonymous, "docs/a.md", "z.md"));
        Assert.True(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task A_directory_with_something_unreadable_in_it_can_not_be_renamed_and_the_message_names_only_what_is_known()
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await RenameAsync(Users.Alice, "mixed", "renamed");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        string message = await BodyOf(response);
        Assert.True(message.Contains("mixed/closed") || message.Contains("mixed/locked.md") || message.Contains("mixed/keep.md"), message);
        Assert.DoesNotContain("hidden.md", message);
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task The_new_path_must_be_allowed_by_its_own_file_entry()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("forbidden.md", Cfg.Perms(cdDeny: ["g:team"]))));

        await AssertStatus(HttpStatusCode.Forbidden, await RenameAsync(Users.Alice, "docs/a.md", "forbidden.md"));
        Assert.True(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task A_renamed_file_keeps_its_own_permissions()
    {
        await AssertStatus(HttpStatusCode.OK, await RenameAsync(Users.Alice, "mixed/view.md", "seen.md"));

        // view.md was read-only for the team; under its new name it still is
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/mixed/seen.md", "x"));
        Assert.Contains("seen.md", Tree.ReadConfig("mixed"));
        Assert.DoesNotContain("view.md", Tree.ReadConfig("mixed"));
    }

    /// <summary>
    /// Entries apply by name whether or not the file exists, so one can outlive its file (removed from outside the app). Renaming another
    /// file to that name must not leave two entries with the same name (an invalid configuration, which closes the folder to everyone but admins):
    /// the left over entry goes, and the file keeps its own.
    /// </summary>
    [Fact]
    public async Task Renaming_a_file_onto_a_leftover_entry_does_not_lock_everyone_out_of_the_folder()
    {
        Tree.Config("mixed", Cfg.Folder(Cfg.Perms(), ("view.md", Cfg.Perms(rwReadOnly: ["g:team"])), ("ghost.md", Cfg.Perms())));

        await AssertStatus(HttpStatusCode.OK, await RenameAsync(Users.Alice, "mixed/view.md", "ghost.md"));

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/mixed"));
        // It took its own entry along: still read-only
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/mixed/ghost.md", "x"));
    }

    /// <summary>
    /// On Windows ".config.json." names the configuration file. Names ending in a dot are refused everywhere, so the answer can't depend on
    /// whether the directory has a configuration (that file exists for admins only).
    /// </summary>
    [Fact]
    public async Task Renaming_to_another_spelling_of_the_configuration_file_does_not_tell_whether_there_is_one()
    {
        Tree.File("mixed/x.md");

        HttpResponseMessage withConfig = await RenameAsync(Users.Alice, "mixed/x.md", ".config.json.");
        HttpResponseMessage withoutConfig = await RenameAsync(Users.Alice, "docs/a.md", ".config.json.");

        Assert.Equal(withoutConfig.StatusCode, withConfig.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withConfig.StatusCode);
        Assert.Null(Tree.ReadConfig("docs"));
    }

    // ---- Delete

    [Fact]
    public async Task Deleting_a_file_and_a_directory()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Alice, "/docs/a.md"));
        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Alice, "/docs/nested"));

        Assert.Empty(Tree.List("docs"));
    }

    [Fact]
    public async Task The_root_can_not_be_deleted()
    {
        await AssertStatus(HttpStatusCode.BadRequest, await Client.DeleteAsync(Users.Admin, "/"));
        Assert.True(Tree.Exists("readme.md"));
    }

    [Theory]
    [InlineData("alice", "shelf/item.md")]
    [InlineData("alice", "drop/existing.md")]
    [InlineData("alice", "drop")]
    [InlineData("alice", "mixed/keep.md")]
    [InlineData("alice", "mixed/locked.md")]
    [InlineData("alice", "vault")]
    [InlineData("alice", "broken")]
    [InlineData("alice", "mixed")]
    [InlineData("alice", "shelf")]
    [InlineData("bob", "docs/a.md")]
    [InlineData("anonymous", "docs/a.md")]
    [InlineData("bob", "team")]
    public async Task Deleting_needs_read_and_delete_on_the_whole_tree(string user, string path)
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await Client.DeleteAsync(HiddenEntriesTests.UserNamed(user), "/" + path);

        await AssertStatus(HttpStatusCode.Forbidden, response);
        string message = await BodyOf(response);
        foreach (string secret in BaseTree.SecretNamesForAlice)
            Assert.DoesNotContain(secret, message);
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task Admins_may_delete_anything_but_the_root()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Admin, "/mixed"));
        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Admin, "/broken"));
        Assert.False(Tree.Exists("mixed"));
    }

    [Fact]
    public async Task A_deleted_file_loses_its_entry()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Alice, "/mixed/view.md"));

        Assert.DoesNotContain("view.md", Tree.ReadConfig("mixed"));
        // A new file of that name has the permissions of the folder, not those of the old one
        Tree.File("mixed/view.md");
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/mixed/view.md", "writable now"));
    }

    [Fact]
    public async Task A_delete_leaves_nothing_behind_in_the_reserved_folder()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Alice, "/docs"));

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(App.NotesDir, "_", "temp", "transfer")));
    }

    /// <summary>A failure while deleting is undone, and its message doesn't show the server's paths.</summary>
    [Fact]
    public async Task A_failed_delete_changes_nothing_and_does_not_reveal_server_paths()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Holding a file open only stops it from being moved on Windows.");

        string before = Tree.Snapshot();
        HttpResponseMessage response;
        await using (new FileStream(Tree.Physical("docs/nested/b.md"), FileMode.Open, FileAccess.Read, FileShare.None))
            response = await Client.DeleteAsync(Users.Alice, "/docs");

        await AssertStatus(HttpStatusCode.InternalServerError, response);
        Assert.Equal(before, Tree.Snapshot());
        string message = await BodyOf(response);
        Assert.DoesNotContain(App.NotesDir, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetTempPath(), message, StringComparison.OrdinalIgnoreCase);
    }
}
