using System.Net;
using System.Net.Http.Json;

namespace notes.Tests.Integration;

/// <summary>Copying, moving and deleting the selection (security specification, Operations, and "Copy, move, delete and rename are checked completely before anything is changed").</summary>
public sealed class TransferTests(NotesAppFactory app) : IntegrationTest(app)
{
    private sealed record TransferAnswer(bool LeftOutUnreadable);

    private async Task<TransferAnswer> SucceedsAsync(HttpResponseMessage response)
    {
        await AssertStatus(HttpStatusCode.OK, response);
        return (await response.Content.ReadFromJsonAsync<TransferAnswer>(Cancellation))!;
    }

    private async Task FailsAndChangesNothingAsync(HttpStatusCode expected, Func<Task<HttpResponseMessage>> transfer, TestUser user)
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await transfer();

        await AssertStatus(expected, response);
        Assert.Equal(before, Tree.Snapshot());
        string message = await BodyOf(response);
        foreach (string secret in BaseTree.SecretNamesForAlice)
            Assert.DoesNotContain(secret, message);
        // A failure keeps the selection
        Assert.NotEmpty((await GetSelectionAsync(user)).Paths);
    }

    // ---- Copy

    [Fact]
    public async Task Copying_a_file()
    {
        TransferAnswer answer = await SucceedsAsync(await TransferAsync(Users.Alice, "copy", "team", "docs/a.md"));

        Assert.False(answer.LeftOutUnreadable);
        Assert.Equal("a", Tree.Read("team/a.md"));
        Assert.Equal("a", Tree.Read("docs/a.md"));
        Assert.Empty((await GetSelectionAsync(Users.Alice)).Paths);
    }

    [Fact]
    public async Task Copying_leaves_out_what_alice_may_not_read_and_says_so()
    {
        TransferAnswer answer = await SucceedsAsync(await TransferAsync(Users.Alice, "copy", "team", "mixed"));

        Assert.True(answer.LeftOutUnreadable);
        Assert.Equal(["team/mixed/keep.md", "team/mixed/open.md", "team/mixed/view.md"],
            Tree.List("team/mixed"));
    }

    [Fact]
    public async Task A_copy_by_someone_who_is_not_admin_has_no_permissions_of_its_own()
    {
        await SucceedsAsync(await TransferAsync(Users.Alice, "copy", "docs", "team"));

        Assert.Null(Tree.ReadConfig("docs/team"));
        // So it has those of where it went: bob may read docs, so he may read the copy (he may not read team)
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Bob, "/docs/team/plan.md"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Bob, "/team"));
    }

    [Fact]
    public async Task An_admins_copy_takes_everything_along_with_the_permissions()
    {
        await SucceedsAsync(await TransferAsync(Users.Admin, "copy", "docs", "mixed"));

        Assert.Equal(Tree.ReadConfig("mixed"), Tree.ReadConfig("docs/mixed"));
        Assert.True(Tree.Exists("docs/mixed/closed/hidden.md"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Alice, "/docs/mixed/closed"));
    }

    [Fact]
    public async Task Copying_needs_create_where_it_goes()
    {
        await FailsAndChangesNothingAsync(HttpStatusCode.Forbidden, () => TransferAsync(Users.Alice, "copy", "shelf", "docs/a.md"), Users.Alice);
        await FailsAndChangesNothingAsync(HttpStatusCode.Forbidden, () => TransferAsync(Users.Bob, "copy", "docs/nested", "docs/a.md"), Users.Bob);
    }

    [Fact]
    public async Task Copying_into_a_directory_alice_may_not_read_is_refused()
    {
        HttpResponseMessage response = await TransferAsync(Users.Alice, "copy", "vault", "docs/a.md");

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal("No read access to the target directory.", await BodyOf(response));
        Assert.Equal(3, Directory.GetFileSystemEntries(Tree.Physical("vault")).Length);
    }

    [Fact]
    public async Task Copying_into_a_hidden_directory_answers_like_a_missing_one()
    {
        HttpResponseMessage hidden = await TransferAsync(Users.Alice, "copy", "vault/inner", "docs/a.md");
        HttpResponseMessage missing = await TransferAsync(Users.Alice, "copy", "docs/missing", "docs/a.md");

        Assert.Equal(missing.StatusCode, hidden.StatusCode);
        Assert.Equal(await BodyOf(missing), await BodyOf(hidden));
        Assert.False(Tree.Exists("vault/inner/a.md"));
    }

    [Fact]
    public async Task Clashes_and_impossible_targets()
    {
        Tree.File("team/a.md", "team's");

        await FailsAndChangesNothingAsync(HttpStatusCode.Conflict, () => TransferAsync(Users.Alice, "copy", "team", "docs/a.md"), Users.Alice);
        await Client.PostAsync(Users.Alice, "/?selection=clear");
        await FailsAndChangesNothingAsync(HttpStatusCode.BadRequest, () => TransferAsync(Users.Alice, "copy", "docs/nested", "docs"), Users.Alice);
        await Client.PostAsync(Users.Alice, "/?selection=clear");
        await FailsAndChangesNothingAsync(HttpStatusCode.BadRequest, () => TransferAsync(Users.Alice, "move", "docs", "docs"), Users.Alice);
        await Client.PostAsync(Users.Alice, "/?selection=clear");
        await FailsAndChangesNothingAsync(HttpStatusCode.BadRequest, () => TransferAsync(Users.Alice, "copy", "docs", "docs/a.md"), Users.Alice);
    }

    [Fact]
    public async Task Two_entries_of_the_same_name_conflict()
    {
        Tree.File("team/sub/a.md");

        await FailsAndChangesNothingAsync(HttpStatusCode.Conflict, () => TransferAsync(Users.Alice, "copy", "mixed", "docs/a.md", "team/sub/a.md"), Users.Alice);
    }

    // ---- Move

    [Fact]
    public async Task Moving_a_directory()
    {
        await SucceedsAsync(await TransferAsync(Users.Alice, "move", "team", "docs"));

        Assert.False(Tree.Exists("docs"));
        Assert.Equal("b", Tree.Read("team/docs/nested/b.md"));
    }

    [Fact]
    public async Task Moving_a_directory_with_something_unreadable_in_it_fails_because_of_it()
    {
        await FailsAndChangesNothingAsync(HttpStatusCode.Forbidden, () => TransferAsync(Users.Alice, "move", "team", "mixed"), Users.Alice);
    }

    [Theory]
    [InlineData("shelf/item.md")]
    [InlineData("drop/existing.md")]
    [InlineData("mixed/keep.md")]
    public async Task Moving_needs_delete_on_the_source(string path)
        => await FailsAndChangesNothingAsync(HttpStatusCode.Forbidden, () => TransferAsync(Users.Alice, "move", "docs", path), Users.Alice);

    [Fact]
    public async Task Create_only_is_enough_to_move_into()
    {
        await SucceedsAsync(await TransferAsync(Users.Alice, "move", "drop", "docs/a.md"));

        Assert.True(Tree.Exists("drop/a.md"));
        Assert.False(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task Moving_into_a_directory_alice_may_not_create_in_is_refused()
        => await FailsAndChangesNothingAsync(HttpStatusCode.Forbidden, () => TransferAsync(Users.Alice, "move", "shelf", "docs/a.md"), Users.Alice);

    [Fact]
    public async Task Moving_a_directory_into_itself_is_refused()
        => await FailsAndChangesNothingAsync(HttpStatusCode.BadRequest, () => TransferAsync(Users.Alice, "move", "docs/nested", "docs"), Users.Alice);

    [Fact]
    public async Task A_moved_file_leaves_its_permissions_behind()
    {
        await SucceedsAsync(await TransferAsync(Users.Alice, "move", "docs", "mixed/view.md"));

        Assert.DoesNotContain("view.md", Tree.ReadConfig("mixed"));
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/view.md", "writable here"));
    }

    // ---- Delete

    [Fact]
    public async Task Deleting_the_selection()
    {
        await SucceedsAsync(await TransferAsync(Users.Alice, "delete", "docs", "docs/a.md", "docs/nested"));

        Assert.Empty(Tree.List("docs"));
    }

    [Fact]
    public async Task Deleting_is_all_or_nothing()
        => await FailsAndChangesNothingAsync(HttpStatusCode.Forbidden, () => TransferAsync(Users.Alice, "delete", "mixed", "mixed/open.md", "mixed/keep.md"), Users.Alice);

    // ---- The selection that is transferred

    [Fact]
    public async Task An_outdated_selection_is_not_transferred()
    {
        SelectionSnapshotDto old = await SelectAsync(Users.Alice, "docs/a.md");
        await SelectAsync(Users.Alice, "docs/nested");

        HttpResponseMessage response = await Client.PostJsonAsync(Users.Alice, "/team?transfer=copy", new { version = old.Version });

        await AssertStatus(HttpStatusCode.Conflict, response);
        Assert.False(Tree.Exists("team/a.md"));
    }

    [Fact]
    public async Task Nobody_can_transfer_someone_elses_selection()
    {
        SelectionSnapshotDto alices = await SelectAsync(Users.Alice, "docs/a.md");

        HttpResponseMessage response = await Client.PostJsonAsync(Users.Carol, "/team?transfer=move", new { version = alices.Version });

        Assert.False(response.IsSuccessStatusCode);
        Assert.True(Tree.Exists("docs/a.md"));
        Assert.Equal(["docs/a.md"], (await GetSelectionAsync(Users.Alice)).Paths);
    }

    [Fact]
    public async Task Anonymous_users_can_not_transfer()
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await Client.PostJsonAsync(Users.Anonymous, "/docs?transfer=delete", new { version = 0 });

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task What_was_selected_is_checked_again_when_it_is_transferred()
    {
        await SelectAsync(Users.Alice, "team/plan.md");
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md");
        // Meanwhile an admin closes team to alice: plan.md is now hidden from her
        Tree.Config("team", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["u:carol"], cdAllow: ["u:carol"])));

        HttpResponseMessage response = await Client.PostJsonAsync(Users.Alice, "/docs/nested?transfer=move", new { version = selection.Version });

        // Answered like an entry that is gone
        await AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("'team/plan.md' no longer exists.", await BodyOf(response));
        Assert.True(Tree.Exists("team/plan.md"));
        Assert.True(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task A_selected_entry_that_was_removed_fails_the_transfer_the_same_way()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md");
        File.Delete(Tree.Physical("docs/a.md"));

        HttpResponseMessage response = await Client.PostJsonAsync(Users.Alice, "/team?transfer=copy", new { version = selection.Version });

        await AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("'docs/a.md' no longer exists.", await BodyOf(response));
    }

    [Fact]
    public async Task Read_taken_away_after_selecting_is_honoured()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs");
        Tree.Config("docs/nested", Cfg.Folder(Cfg.Perms(rwDeny: ["g:team"])));

        // A move fails; a copy leaves it out
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostJsonAsync(Users.Alice, "/team?transfer=move", new { version = selection.Version }));
        selection = await GetSelectionAsync(Users.Alice);
        TransferAnswer answer = await SucceedsAsync(await Client.PostJsonAsync(Users.Alice, "/team?transfer=copy", new { version = selection.Version }));
        Assert.True(answer.LeftOutUnreadable);
        Assert.False(Tree.Exists("team/docs/nested/b.md"));
    }

    /// <summary>A body that is not sent as JSON is a bad request like a body that is not JSON, not a failure of the server.</summary>
    [Theory]
    [InlineData("/docs?transfer=copy")]
    [InlineData("/?selection=add")]
    public async Task A_body_that_is_not_json_is_a_bad_request(string url)
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, url, "{ \"version\": 0, \"paths\": [] }"));

    [Theory]
    [InlineData("?transfer=steal")]
    [InlineData("?transfer=")]
    public async Task Unknown_kinds_are_refused(string query)
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice, "/docs" + query, new { version = 0 }));

    [Fact]
    public async Task A_transfer_without_a_selection_or_with_a_bad_body_is_refused()
    {
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice, "/docs?transfer=copy", new { version = (await GetSelectionAsync(Users.Alice)).Version }));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.SendAsync(Users.Alice, HttpMethod.Post, "/docs?transfer=copy", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json")));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostJsonAsync(Users.Alice, "/docs/a.md?transfer=copy", new { version = 0 }));
    }
}
