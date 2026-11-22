using System.Net;

namespace Notes.Tests.Integration;

/// <summary>Downloading the selection as an archive (security specification, Operations: "Download the selection").</summary>
public sealed class ArchiveSelectionTests(NotesAppFactory app) : ArchiveTest(app)
{
    [Fact]
    public async Task Selected_files_and_directories_are_the_top_of_the_archive_and_the_selection_stays()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md", "docs/nested");

        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/team", "selection", version: selection.Version);
        Dictionary<string, string> contents = SevenZipContents(bytes);

        Assert.Equal("a", contents["a.md"]);
        Assert.Equal("b", contents["nested/b.md"]);
        Assert.DoesNotContain(contents.Keys, name => name.StartsWith("docs", StringComparison.Ordinal));
        Assert.Equal(["docs/a.md", "docs/nested"], (await GetSelectionAsync(Users.Alice)).Paths);
    }

    [Fact]
    public async Task Entries_of_the_same_name_from_different_directories_are_all_in_it()
    {
        _ = Tree.File("team/a.md", "team's a");
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md", "team/a.md");

        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/", "selection", "zip", "medium", version: selection.Version);
        Dictionary<string, string> contents = ZipContents(bytes);

        Assert.Equal(2, contents.Count);
        Assert.Equal(["a", "team's a"], contents.Values.Order(StringComparer.Ordinal));
        Assert.Contains("a.md", contents.Keys);
    }

    [Fact]
    public async Task What_alice_may_not_read_inside_a_selected_directory_is_left_out()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "mixed");

        (PreparedArchiveDto prepared, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/", "selection", version: selection.Version);
        Dictionary<string, string> contents = SevenZipContents(bytes);

        Assert.True(prepared.LeftOutUnreadable);
        Assert.Contains("mixed/open.md", contents.Keys);
        Assert.DoesNotContain("mixed/locked.md", contents.Keys);
        Assert.DoesNotContain(contents.Keys, name => name.StartsWith("mixed/closed/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_selection_that_changed_since_the_page_showed_it_is_refused()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md");
        _ = await SelectAsync(Users.Alice, "readme.md");

        await AssertStatus(HttpStatusCode.Conflict, await PrepareAsync(Users.Alice, "/", "selection", version: selection.Version));
        await AssertStatus(HttpStatusCode.Conflict, await PrepareAsync(Users.Alice, "/", "selection"));
        Assert.Empty(ArchiveFiles());
    }

    [Fact]
    public async Task An_empty_selection_has_nothing_to_archive()
    {
        SelectionSnapshotDto selection = await GetSelectionAsync(Users.Alice);

        await AssertStatus(HttpStatusCode.BadRequest, await PrepareAsync(Users.Alice, "/", "selection", version: selection.Version));
    }

    [Fact]
    public async Task A_selected_entry_that_may_no_longer_be_read_is_refused_by_its_name()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md", "team/plan.md");
        // Still known, as its directory may be read, but no longer openable
        Tree.Config("team", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"]),
            ("plan.md", Cfg.Perms(inherit: false))));

        HttpResponseMessage response = await PrepareAsync(Users.Alice, "/", "selection", version: selection.Version);

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Contains("team/plan.md", await BodyOf(response));
        Assert.Empty(ArchiveFiles());
    }

    [Fact]
    public async Task A_selected_entry_that_is_gone_or_hidden_now_is_refused_without_its_name()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md", "team/sub");
        Tree.Config("team", Cfg.Folder(Cfg.Perms(inherit: false)));

        HttpResponseMessage hidden = await PrepareAsync(Users.Alice, "/", "selection", version: selection.Version);

        await AssertStatus(HttpStatusCode.Conflict, hidden);
        Assert.DoesNotContain("sub", await BodyOf(hidden));

        BaseTree.Create(Tree);
        Directory.Delete(Tree.Physical("team/sub"), recursive: true);
        HttpResponseMessage gone = await PrepareAsync(Users.Alice, "/", "selection", version: selection.Version);
        await AssertStatus(HttpStatusCode.Conflict, gone);
        Assert.Equal(await BodyOf(gone), await BodyOf(hidden));
    }

    [Fact]
    public async Task The_selection_can_be_archived_from_any_page_alice_may_read_but_not_from_one_she_may_not()
    {
        SelectionSnapshotDto selection = await SelectAsync(Users.Alice, "docs/a.md");

        await AssertStatus(HttpStatusCode.OK, await PrepareAsync(Users.Alice, "/team/plan.md", "selection", version: selection.Version));
        await AssertStatus(HttpStatusCode.Forbidden, await PrepareAsync(Users.Alice, "/vault", "selection", version: selection.Version));
    }

    [Fact]
    public async Task The_selection_card_has_the_download()
        => Assert.Contains("id=\"selection-download\"", await BodyOf(await Client.GetAsync(Users.Alice, "/docs")));
}
