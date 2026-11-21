using System.Net;

namespace Notes.Tests.Integration;

/// <summary>Reading the notes through bridges: addresses, listings, permissions across a bridge, and what a bridge hides (docs/administration/bridges.md).</summary>
public sealed class BridgeBrowsingTests(BridgedNotesAppFactory app) : BridgeTest(app)
{
    [Fact]
    public async Task A_bridged_file_is_read_from_the_physical_directory_of_the_bridge()
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/hdd/test/note.md?download");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("note", await BodyOf(response));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/hdd/archive/old.md"));
        // Any case, as the notes are
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/HDD/Archive/old.md"));
    }

    [Fact]
    public async Task A_bridge_is_listed_in_its_directory_by_its_virtual_name()
    {
        string root = await BodyOf(await Client.GetAsync(Users.Alice, "/"));
        string hdd = await BodyOf(await Client.GetAsync(Users.Alice, "/hdd"));

        Assert.Contains("href=\"/hdd\"", root);
        Assert.Contains("href=\"/hdd/archive\"", hdd);
        Assert.Contains("href=\"/hdd/test\"", hdd);
        // Not the names of the physical directories
        Assert.DoesNotContain("hdd-physical", root);
        Assert.DoesNotContain("archive-physical", hdd);
    }

    [Fact]
    public async Task What_a_bridge_hides_on_disk_is_no_part_of_the_notes()
    {
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/hdd/shadowed.md"));
        Assert.DoesNotContain("shadowed.md", await BodyOf(await Client.GetAsync(Users.Admin, "/hdd")));
    }

    [Fact]
    public async Task The_data_of_the_app_in_a_bridge_can_not_be_reached()
    {
        _ = App.Hdd.File("_/temp/x.txt", "x");

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/hdd/_"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/hdd/_/temp/x.txt"));
        Assert.DoesNotContain("href=\"/hdd/_\"", await BodyOf(await Client.GetAsync(Users.Admin, "/hdd")));
    }

    [Theory]
    [InlineData("/hdd/..%2F..%2Fdeep-physical/secret.md")]
    [InlineData("/hdd/%2E%2E/deep-physical/secret.md")]
    [InlineData("/hdd/..\\deep-physical\\secret.md")]
    public async Task A_bridge_can_not_be_left_through_its_physical_directory(string address)
    {
        // The physical directories of the bridges are next to each other, but nothing leads from one to the other
        RawResponse response = await Client.RawAsync(Users.Admin, "GET", address);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("the secret", response.Body);
    }

    [Fact]
    public async Task Permissions_go_on_from_the_directory_a_bridge_is_mounted_in()
    {
        // hdd has no configuration: the root's, so guests and anonymous users may read it
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Bob, "/hdd/test/note.md"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, "/hdd/test/note.md"));
        // The configuration of a bridge is in its physical directory, and decides as anywhere else
        await AssertStatus(HttpStatusCode.Forbidden, await Client.GetAsync(Users.Bob, "/hdd/archive"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/hdd/archive"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Bob, "/hdd/archive/old.md"));
    }

    [Fact]
    public async Task A_bridge_inside_an_unreadable_directory_is_hidden_like_the_rest_of_it()
    {
        RawResponse missing = await Client.RawAsync(Users.Alice, "GET", "/vault/missing");
        RawResponse bridge = await Client.RawAsync(Users.Alice, "GET", "/vault/deep");

        Assert.Equal(HttpStatusCode.NotFound, bridge.StatusCode);
        Assert.Equal(missing.StatusCode, bridge.StatusCode);
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/vault/deep/secret.md"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Carol, "/vault/deep/secret.md"));
    }

    [Theory]
    [MemberData(nameof(NotAdmins))]
    public async Task Only_admins_see_any_sign_of_a_bridge(TestUser user)
    {
        foreach (string address in new[] { "/", "/hdd", "/hdd/test/note.md", "/hdd/archive" })
        {
            HttpResponseMessage response = await Client.GetAsync(user, address);
            string body = await BodyOf(response);
            Assert.DoesNotContain("bridge", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(App.HddDir, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static TheoryData<TestUser> NotAdmins => [Users.Alice, Users.Bob, Users.Anonymous];

    [Fact]
    public async Task The_configuration_of_a_bridge_is_kept_in_its_physical_directory()
    {
        string text = Cfg.Folder(Cfg.Perms(inherit: true, rwReadOnly: ["g:guests"]));

        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/hdd?config", text));

        Assert.Equal(text.ReplaceLineEndings(), App.Hdd.ReadConfig("")?.ReplaceLineEndings());
        Assert.Null(App.Tree.ReadConfig("hdd"));
    }

    [Fact]
    public async Task The_configuration_page_shows_admins_where_the_entry_is_on_disk()
    {
        string body = await BodyOf(await Client.GetAsync(Users.Admin, "/hdd/test/note.md?config"));

        Assert.Contains("id=\"config-physical-path\"", body);
        Assert.Contains(Path.Combine(App.HddDir, "test", "note.md"), body);
    }

    [Fact]
    public async Task A_zip_of_a_directory_has_what_is_in_its_bridges()
    {
        HttpResponseMessage alice = await Client.GetAsync(Users.Alice, "/?download");
        HttpResponseMessage admin = await Client.GetAsync(Users.Admin, "/hdd?download");

        await AssertStatus(HttpStatusCode.OK, alice);
        List<string> names = await ZipNamesAsync(alice);
        Assert.Contains("hdd/test/note.md", names);
        Assert.Contains("hdd/archive/old.md", names);
        Assert.DoesNotContain(names, name => name.Contains("shadowed") || name.Contains("secret") || name.StartsWith("hdd/_", StringComparison.Ordinal));
        Assert.Contains("hdd.zip", admin.Content.Headers.ContentDisposition?.FileName ?? "");
    }

    [Fact]
    public async Task There_is_no_way_into_the_history_below_a_bridge_yet()
    {
        string alice = await BodyOf(await Client.GetAsync(Users.Alice, "/hdd/test/note.md"));
        string admin = await BodyOf(await Client.GetAsync(Users.Admin, "/hdd/test/note.md"));

        Assert.DoesNotContain("id=\"history-button\"", alice);
        Assert.Matches(@"id=""history-button""[^>]* data-disabled-reason=""[^""]+""", admin);
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/hdd/test/note.md?history"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/hdd/test/note.md?commits"));
        await AssertStatus(HttpStatusCode.Redirect, await Client.GetAsync(Users.Alice, "/hdd/test/note.md?before=2020-01-01T00:00:00Z"));
    }
}
