using System.Net;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using notes.Features.HistoryViewing.Requests;

namespace notes.Tests.Integration;

/// <summary>
/// The changes of a commit (<c>?commit=&amp;changes</c>): who may see them, the two sides of a file, what a directory lists, and the way
/// between them and the state of the commit.
/// </summary>
public sealed class HistoryCommitChangesTests(NotesAppFactory app) : HistoryStateTest(app)
{
    private string NewestCommitId()
    {
        using Repository repository = new(History.GitDirectory);
        return repository.Head.Tip.Sha;
    }

    /// <summary>Commits what is there now, and returns the id of the commit.</summary>
    private string Commit()
    {
        _ = CommitNow();
        return NewestCommitId();
    }

    private static string Changes(string commit) => "?commit=" + commit + "&changes";

    private async Task<string> PageAsync(TestUser user, string address)
    {
        HttpResponseMessage response = await Client.GetAsync(user, address);
        await AssertStatus(HttpStatusCode.OK, response);
        return await BodyOf(response);
    }

    /// <summary>The row of the listing of the entry (its virtual path).</summary>
    private static Match RowOf(string body, string virtualPath)
        => Regex.Match(body, "data-path=\"" + Regex.Escape(virtualPath) + "\">(?:(?!</tr>).)*</tr>", RegexOptions.Singleline);

    private static string? MarkOf(string body, string virtualPath)
        => Regex.Match(RowOf(body, virtualPath).Value, "data-change-mark=\"([a-z]+)\"") is { Success: true } mark ? mark.Groups[1].Value : null;

    // ---- A file

    [Fact]
    public async Task A_changed_file_shows_both_sides_in_the_diff_editor()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = Commit();
        _ = Tree.File("docs/a.md", "changed");
        string commit = Commit();

        string body = await PageAsync(Users.Alice, "/docs/a.md" + Changes(commit));

        Assert.Contains("id=\"changes-editor\"", body);
        Assert.Contains("\"showsBoth\":true", body);
        Assert.Contains("\"original\":\"a\"", body);
        Assert.Contains("\"modified\":\"changed\"", body);
        // Nothing but what reads, and the card says that the changes are shown
        Assert.Contains("data-changes=\"true\"", body);
        Assert.DoesNotContain("id=\"download-button\"", body);
        Assert.DoesNotContain("id=\"save-button\"", body);
        Assert.DoesNotContain("id=\"markdown-preview\"", body);
        Assert.Matches("id=\"commit-changes-button\" href=\"\\?commit=" + commit + "\"[^>]*aria-disabled=\"false\"", body);
    }

    [Fact]
    public async Task An_added_file_is_empty_before_and_a_deleted_one_after()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = Commit();
        _ = Tree.File("docs/new.md", "new");
        string added = Commit();
        File.Delete(Tree.Physical("docs/a.md"));
        string deleted = Commit();

        string addedBody = await PageAsync(Users.Alice, "/docs/new.md" + Changes(added));
        Assert.Contains("\"original\":\"\"", addedBody);
        Assert.Contains("\"modified\":\"new\"", addedBody);

        // What is gone now is seen through the state before the commit; its state can't be gone to
        string deletedBody = await PageAsync(Users.Alice, "/docs/a.md" + Changes(deleted));
        Assert.Contains("\"original\":\"a\"", deletedBody);
        Assert.Contains("\"modified\":\"\"", deletedBody);
        Assert.Matches("id=\"commit-changes-button\"[^>]*aria-disabled=\"true\"", deletedBody);
        Assert.Matches("id=\"commit-changes-button\"[^>]*data-disabled-reason=\"[^\"]+\"", deletedBody);
    }

    [Fact]
    public async Task A_side_that_could_not_be_read_is_not_shown_and_not_there_for_anonymous_users()
    {
        SetRoot(historyAllow: ["g:team"], historyExistingOnly: ["a"]);
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(), ("a.md", Cfg.Perms(rwDeny: ["a"]))));
        _ = Tree.File("docs/a.md", "unreadable before");
        _ = Commit();
        Tree.Config("docs", Cfg.Folder(Cfg.Perms()));
        _ = Tree.File("docs/a.md", "readable after");
        string commit = Commit();

        string alice = await PageAsync(Users.Alice, "/docs/a.md" + Changes(commit));
        Assert.Contains("id=\"change-side-not-shown\"", alice);
        Assert.Contains("\"showsBoth\":false", alice);
        Assert.Contains("\"modified\":\"readable after\"", alice);
        Assert.DoesNotContain("unreadable before", alice);

        // Anonymous users don't learn that it was there: for them the commit added it
        string anonymous = await PageAsync(Users.Anonymous, "/docs/a.md" + Changes(commit));
        Assert.DoesNotContain("id=\"change-side-not-shown\"", anonymous);
        Assert.Contains("\"original\":\"\"", anonymous);
        Assert.DoesNotContain("unreadable before", anonymous);
    }

    [Fact]
    public async Task Binary_files_show_both_sides_from_their_own_raw_bytes()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = Tree.File("docs/pic.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);
        _ = Commit();
        _ = Tree.File("docs/pic.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x02]);
        string commit = Commit();
        _ = Tree.File("docs/other.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x03]);
        string added = Commit();

        string body = await PageAsync(Users.Alice, "/docs/pic.png" + Changes(commit));
        Assert.Contains("id=\"change-before\" data-side-kind=\"shown\"", body);
        Assert.Contains("id=\"change-after\" data-side-kind=\"shown\"", body);
        Assert.Contains("src=\"?raw&amp;commit=" + commit + "&amp;changes&amp;previous\"", body);
        Assert.DoesNotContain("id=\"changes-editor\"", body);

        HttpResponseMessage after = await Client.GetAsync(Users.Alice, "/docs/pic.png?raw&commit=" + commit + "&changes");
        HttpResponseMessage before = await Client.GetAsync(Users.Alice, "/docs/pic.png?raw&commit=" + commit + "&changes&previous");
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x00, 0x02], await after.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x00, 0x01], await before.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        // A side that isn't there has no bytes
        Assert.Contains("id=\"change-before\" data-side-kind=\"not-there\"", await PageAsync(Users.Alice, "/docs/other.png" + Changes(added)));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/other.png?raw&commit=" + added + "&changes&previous"));
    }

    // ---- A directory

    [Fact]
    public async Task A_directory_lists_only_what_the_commit_changed_visibly_with_its_marks()
    {
        _ = Tree.File("docs/same.md", "same");
        // (So that docs/nested stays: git keeps no empty directories)
        _ = Tree.File("docs/nested/stays.md", "stays");
        SetRoot(historyAllow: ["g:team"]);
        _ = Commit();
        _ = Tree.File("docs/a.md", "changed");
        _ = Tree.File("docs/new.md", "new");
        File.Delete(Tree.Physical("docs/nested/b.md"));
        _ = Tree.File("mixed/locked.md", "changed where alice can't see it");
        _ = Tree.File("readme.md", "changed");
        string commit = Commit();

        string docs = await PageAsync(Users.Alice, "/docs" + Changes(commit));
        Assert.Equal("changed", MarkOf(docs, "docs/a.md"));
        Assert.Equal("added", MarkOf(docs, "docs/new.md"));
        // A directory with changes below it is listed without a mark; what didn't change isn't listed
        Assert.True(RowOf(docs, "docs/nested").Success);
        Assert.Null(MarkOf(docs, "docs/nested"));
        Assert.False(RowOf(docs, "docs/same.md").Success);
        Assert.DoesNotContain("id=\"download-button\"", docs);

        string nested = await PageAsync(Users.Alice, "/docs/nested" + Changes(commit));
        Assert.Equal("deleted", MarkOf(nested, "docs/nested/b.md"));
        Assert.Contains("text-decoration-line-through", RowOf(nested, "docs/nested/b.md").Value);

        string root = await PageAsync(Users.Alice, "/" + Changes(commit));
        Assert.Equal("changed", MarkOf(root, "readme.md"));
        Assert.True(RowOf(root, "docs").Success);
        // A change that can't be seen is not listed, nor is the directory it is in
        Assert.False(RowOf(root, "mixed").Success);
        Assert.DoesNotContain("locked.md", root);
    }

    [Fact]
    public async Task What_the_commit_changed_nothing_visible_of_goes_to_its_state()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = Commit();
        _ = Tree.File("readme.md", "changed");
        _ = Tree.File("mixed/locked.md", "changed where alice can't see it");
        string commit = Commit();

        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md" + Changes(commit)), "/docs/a.md?commit=" + commit, EarlierStateNotice.NotChangedByCommit);
        AssertLeft(await Client.GetAsync(Users.Alice, "/mixed" + Changes(commit)), "/mixed?commit=" + commit, EarlierStateNotice.NotChangedByCommit);
        // The state of the commit says so too, on its way to the changes
        Assert.Matches("id=\"commit-changes-button\"[^>]*aria-disabled=\"true\"", await PageAsync(Users.Alice, "/docs/a.md?commit=" + commit));
        Assert.Matches("id=\"commit-changes-button\" href=\"\\?commit=" + commit + "&amp;changes\"[^>]*aria-disabled=\"false\"",
            await PageAsync(Users.Alice, "/readme.md?commit=" + commit));
    }

    // ---- Who may see them, and the addresses

    [Fact]
    public async Task Whoever_may_not_see_the_history_is_answered_as_for_the_state_of_the_commit()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = Commit();
        _ = Tree.File("docs/a.md", "changed");
        _ = Tree.File("vault/secret.md", "changed");
        string commit = Commit();

        AssertLeft(await Client.GetAsync(Users.Bob, "/docs/a.md" + Changes(commit)), "/docs/a.md", EarlierStateNotice.NotPermitted);
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/secret.md" + Changes(commit)));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/never.md" + Changes(commit)));
        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?commit=nonsense&changes"), "/docs/a.md", EarlierStateNotice.InvalidCommit);
        AssertLeft(await Client.GetAsync(Users.Alice, "/docs/a.md?commit=" + new string('0', 40) + "&changes"), "/docs/a.md", EarlierStateNotice.NoSuchCommit);
    }

    [Fact]
    public async Task Changes_need_a_commit_and_can_not_be_downloaded_or_changed()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = Commit();
        _ = Tree.File("docs/a.md", "changed");
        DateTimeOffset after = CommitNow();
        string commit = NewestCommitId();

        await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Alice, "/docs/a.md?changes"));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(after) + "&changes"));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Alice, "/docs/a.md" + Changes(commit) + "&download"));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, "/docs/a.md" + Changes(commit), "x"));
        Assert.Equal("changed", File.ReadAllText(Tree.Physical("docs/a.md")));
    }

    [Fact]
    public async Task The_list_of_changes_has_the_way_to_the_changes_of_each_commit()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = Commit();
        _ = Tree.File("docs/a.md", "changed");
        string commit = Commit();

        string list = await PageAsync(Users.Alice, "/docs/a.md?commits");
        Assert.Contains("class=\"commit-compare link-secondary\" href=\"?commit=" + commit + "&changes\"", list);
        // In the changes of the commit, the list is there from the start, with the commit marked
        Assert.Matches("data-commit=\"" + commit + "\"\\s+aria-current=\"true\"", await PageAsync(Users.Alice, "/docs/a.md" + Changes(commit)));
    }
}
