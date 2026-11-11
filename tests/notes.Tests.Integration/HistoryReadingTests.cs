using System.IO.Compression;
using System.Net;
using System.Text.Json;
using notes.Features.HistoryViewing;

namespace notes.Tests.Integration;

/// <summary>What an earlier state shows: its listings, raw bytes, downloads and images, and the times at which an entry can be seen.</summary>
public sealed class HistoryReadingTests(NotesAppFactory app) : HistoryStateTest(app)
{
    // ---- The listing of a state

    [Fact]
    public async Task Each_entry_of_a_listing_is_shown_by_what_may_be_done_with_it_then()
    {
        SetRoot(historyAllow: ["g:team"]);
        Tree.Config("mixed", Cfg.Folder(Cfg.Perms(),
            ("locked.md", Cfg.Perms(inherit: false)),
            ("keep.md", Cfg.Perms(historyDeny: ["u:alice"])),
            ("view.md", Cfg.Perms(rwReadOnly: ["g:team"])),
            ("gone.md", Cfg.Perms(historyDeny: ["u:alice"]))));
        _ = Tree.File("mixed/gone.md", "gone");
        _ = Tree.File("mixed/kept-out/x.md", "x");
        DateTimeOffset then = CommitNow();
        File.Delete(Tree.Physical("mixed/gone.md"));
        Tree.Config("mixed/kept-out", Cfg.Folder(gitignore: true, Cfg.Perms()));
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/mixed?" + At(then)));

        Assert.Matches(@"<a [^>]*href=""/mixed/open.md""", body);
        Assert.Matches(@"<a [^>]*href=""/mixed/view.md""", body);
        Assert.Contains("data-locked-entry=\"locked.md\"", body);
        Assert.Contains("data-locked-entry=\"closed\"", body);
        // Readable, but no history: a link to it as it is now (which says why), unless it is gone
        Assert.Contains("data-no-history-entry=\"keep.md\"", body);
        Assert.Matches(@"<a [^>]*href=""/mixed/keep.md""", body);
        Assert.Contains("data-no-history-entry=\"gone.md\"", body);
        Assert.DoesNotMatch(@"href=""/mixed/gone.md""", body);
        // Each row says one reason only
        Assert.DoesNotContain("data-locked-entry=\"\"", body);
        Assert.DoesNotContain("data-no-history-entry=\"\"", body);
        // Left out of the history now: not listed
        Assert.DoesNotContain("kept-out", body);
        Assert.DoesNotContain(".config.json", body);

        // Anonymous users don't see what they may not read at all
        SetRoot(historyAllow: ["g:team", "a"]);
        string anonymous = await BodyOf(await Client.GetAsync(Users.Anonymous, "/mixed?" + At(then)));
        Assert.DoesNotContain("locked.md", anonymous);
        Assert.DoesNotContain("closed", anonymous);
    }

    // ---- The breadcrumb of a state

    [Fact]
    public async Task Directories_above_whose_history_can_not_be_seen_have_the_crossed_out_calendar_in_the_breadcrumb()
    {
        // Alice is given the history of docs/nested only, not of the root or docs
        Tree.Config("docs/nested", Cfg.Folder(Cfg.Perms(historyAllow: ["u:alice"])));
        DateTimeOffset then = CommitNow();

        foreach (string path in new[] { "/docs/nested/b.md", "/docs/nested" })
        {
            string body = await BodyOf(await Client.GetAsync(Users.Alice, path + "?" + At(then)));
            Assert.Contains("id=\"history-card\"", body);
            // Each still leads to the directory as it is now, which says why it can't be seen as it was
            Assert.Matches(@"<a [^>]*href=""/""[^>]*data-no-history-parent=""""", body);
            Assert.Matches(@"<a [^>]*href=""/docs""[^>]*title=""Insufficient permissions to see this directory as it was.""[^>]*data-no-history-parent=""docs""", body);
            Assert.DoesNotContain("data-no-history-parent=\"docs/nested\"", body);
        }

        // A directory above that can be seen as it was is an ordinary link
        Assert.Matches(@"<a class=""text-decoration-none"" href=""/docs/nested""", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/nested/b.md?" + At(then))));
        // The breadcrumb of the notes as they are has none of it
        Assert.DoesNotContain("data-no-history-parent", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/nested/b.md")));
        // Admins may see everything as it was
        Assert.DoesNotContain("data-no-history-parent", await BodyOf(await Client.GetAsync(Users.Admin, "/docs/nested/b.md?" + At(then))));
    }

    [Fact]
    public async Task A_directory_above_that_is_gone_and_whose_history_can_not_be_seen_leads_nowhere()
    {
        SetRoot(historyAllow: ["g:team"]);
        Tree.Config("old", Cfg.Folder(Cfg.Perms(historyDeny: ["u:alice"])));
        Tree.Config("old/sub", Cfg.Folder(Cfg.Perms(historyAllow: ["u:alice"])));
        _ = Tree.File("old/sub/c.md", "c");
        DateTimeOffset then = CommitNow();
        Directory.Delete(Tree.Physical("old"), recursive: true);
        _ = CommitNow();

        string body = await BodyOf(await Client.GetAsync(Users.Alice, "/old/sub/c.md?" + At(then)));

        Assert.Contains("<span class=\"text-secondary\" title=\"Insufficient permissions to see this directory as it was. It is not there any more.\" data-no-history-parent=\"old\">", body);
        Assert.DoesNotContain("href=\"/old\"", body);
        Assert.Matches(@"<a class=""text-decoration-none"" href=""/old/sub""", body);
        Assert.DoesNotContain("data-no-history-parent=\"\"", body);
    }

    [Fact]
    public async Task Only_those_who_manage_the_configuration_are_told_that_a_directory_above_is_left_out_of_the_history()
    {
        SetRoot(historyAllow: ["g:team"]);
        Tree.Config("docs", Cfg.Folder(gitignore: true, Cfg.Perms()));
        Tree.Config("docs/nested", Cfg.Folder(gitignore: false, Cfg.Perms()));
        DateTimeOffset then = CommitNow();

        string admin = await BodyOf(await Client.GetAsync(Users.Admin, "/docs/nested/b.md?" + At(then)));
        string alice = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/nested/b.md?" + At(then)));

        Assert.Matches(@"href=""/docs""[^>]*title=""The configuration leaves this directory out of the history.""[^>]*data-no-history-parent=""docs""", admin);
        Assert.Matches(@"href=""/docs""[^>]*title=""Insufficient permissions to see this directory as it was.""[^>]*data-no-history-parent=""docs""", alice);
        Assert.DoesNotContain("data-no-history-parent=\"\"", alice);
    }

    // ---- Reading a state

    [Fact]
    public async Task Raw_bytes_and_downloads_are_of_the_state()
    {
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];
        _ = Tree.File("docs/picture.png", picture);
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset then = CommitNow();
        _ = Tree.File("docs/picture.png", [1, 2, 3]);
        _ = Tree.File("docs/a.md", "changed");
        _ = Tree.File("docs/new.md", "new");
        Tree.Config("docs/nested", Cfg.Folder(Cfg.Perms(historyDeny: ["u:alice"])));
        _ = CommitNow();

        HttpResponseMessage raw = await Client.GetAsync(Users.Alice, "/docs/picture.png?raw&" + At(then));
        await AssertStatus(HttpStatusCode.OK, raw);
        Assert.Equal(picture, await raw.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal("nosniff", raw.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("?raw&amp;before=", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/picture.png?" + At(then))));

        HttpResponseMessage file = await Client.GetAsync(Users.Alice, "/docs/a.md?download&" + At(then));
        Assert.Equal("a", await file.Content.ReadAsStringAsync(Cancellation));

        // The zip has what may be seen as it was: not what came later, not what alice can't see the history of now
        List<string> names = await ZipNamesAsync(await Client.GetAsync(Users.Alice, "/docs?download&" + At(then)));
        Assert.Contains("a.md", names);
        Assert.Contains("picture.png", names);
        Assert.DoesNotContain("new.md", names);
        Assert.DoesNotContain(names, name => name.StartsWith("nested"));

        using ZipArchive zip = new(await (await Client.GetAsync(Users.Alice, "/docs?download&" + At(then))).Content.ReadAsStreamAsync(Cancellation));
        using StreamReader reader = new(zip.GetEntry("a.md")!.Open());
        Assert.Equal("a", await reader.ReadToEndAsync(Cancellation));
    }

    [Fact]
    public async Task Images_in_markdown_load_from_the_state()
    {
        _ = Tree.File("docs/a.md", "![picture](picture.png)");
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset then = CommitNow();

        Assert.Contains("picture.png?raw&amp;before=" + EarlierStateTime.Format(then), await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md?" + At(then))));
        Assert.Contains("picture.png?raw\"", await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md")));
    }

    // ---- The times at which an entry can be seen

    private async Task<List<(string After, string? Until)>> TimesAsync(TestUser user, string path)
    {
        HttpResponseMessage response = await Client.GetAsync(user, path + "?history");
        await AssertStatus(HttpStatusCode.OK, response);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        return [.. json.RootElement.GetProperty("ranges").EnumerateArray().Select(range => (range.GetProperty("after").GetString()!, range.GetProperty("until").GetString()))];
    }

    [Fact]
    public async Task The_times_are_those_at_which_the_entry_is_open()
    {
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset granted = CommitNow();
        SetRoot();
        DateTimeOffset taken = CommitNow();
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset again = CommitNow();

        List<(string After, string? Until)> times = await TimesAsync(Users.Alice, "/readme.md");

        // The latest first: from the last grant up to now, and the earlier time it was given
        Assert.Equal(2, times.Count);
        Assert.Null(times[0].Until);
        Assert.True(EarlierStateTime.TryParse(times[0].After, out DateTimeOffset latestAfter) && latestAfter < again && latestAfter > taken);
        Assert.True(EarlierStateTime.TryParse(times[1].After, out DateTimeOffset earlierAfter) && earlierAfter < granted);
        Assert.True(EarlierStateTime.TryParse(times[1].Until, out DateTimeOffset earlierUntil) && earlierUntil > granted && earlierUntil < taken);

        // Admins: every state since the first one
        List<(string After, string? Until)> admin = await TimesAsync(Users.Admin, "/readme.md");
        Assert.Single(admin);
        Assert.Null(admin[0].Until);
    }

    [Fact]
    public async Task The_times_are_only_told_to_those_who_may_see_the_history_now()
    {
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Bob, "/readme.md?history"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/readme.md?history"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/secret.md?history"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault?history"));
        Tree.Config("docs", Cfg.Folder(gitignore: true, Cfg.Perms()));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/docs/a.md?history"));
    }
}
