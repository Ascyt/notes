using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Notes.Tests.Integration;

/// <summary>
/// A site that shows only some directories (<c>Config:Exposed</c>, security specification, "Exposed folders"): docs, and team/sub without
/// team (<see cref="ExposedNotesAppFactory"/>).
/// </summary>
public sealed class ExposedFolderTests(ExposedNotesAppFactory app) : IntegrationTest(app), IClassFixture<ExposedNotesAppFactory>
{
    // The root isn't part of this site: the selection is asked of a page that is, as the pages do
    protected override string SelectionAddress => "/docs";

    [Theory]
    [InlineData("/")]
    [InlineData("/readme.md")]
    [InlineData("/mixed")]
    [InlineData("/mixed/open.md")]
    [InlineData("/team")]
    [InlineData("/team/plan.md")]
    public async Task What_is_not_exposed_is_missing_also_for_admins(string url)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, url));
        foreach (string query in new[] { "?download", "?raw", "?config", "?history", "?commits", "?before=2026-01-01T00:00:00Z" })
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, url + query));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Admin, url, "changed"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Admin, url + "?create=file"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Admin, url + "?rename=other.md"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.DeleteAsync(Users.Admin, url));

        Assert.Equal(before, Tree.Snapshot());
    }

    [Theory]
    [InlineData("/docs")]
    [InlineData("/docs/a.md")]
    [InlineData("/docs/nested/b.md")]
    [InlineData("/team/sub")]
    [InlineData("/team/sub/c.md")]
    public async Task What_is_exposed_is_there_as_it_is_in_the_notes(string url)
        => await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, url));

    [Fact]
    public async Task The_directories_above_an_exposed_one_still_count()
    {
        // Anonymous users may read the root, but not team: what is in team/sub is hidden from them, exposed or not
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/team/sub"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/team/sub/c.md"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, "/docs/a.md"));
    }

    [Fact]
    public async Task An_exposed_directory_stays_where_it_is_while_what_is_in_it_can_be_changed()
    {
        await AssertStatus(HttpStatusCode.Forbidden, await Client.DeleteAsync(Users.Admin, "/docs"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Admin, "/docs?rename=other"));
        await AssertStatus(HttpStatusCode.Forbidden, await Client.DeleteAsync(Users.Alice, "/team/sub"));
        Assert.True(Tree.IsDirectory("docs"));
        Assert.True(Tree.IsDirectory("team/sub"));

        await AssertStatus(HttpStatusCode.OK, await Client.DeleteAsync(Users.Alice, "/docs/a.md"));
        Assert.False(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task Nothing_leads_out_of_what_is_exposed()
    {
        // The listing of an exposed directory, and copying from it into one that isn't
        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Contains("data-path=\"docs/nested\"", listing);
        _ = await SelectAsync(Users.Alice, "docs/a.md");

        await AssertStatus(HttpStatusCode.NotFound, await TransferAsync(Users.Alice, "copy", "team", "docs/a.md"));
        Assert.False(Tree.Exists("team/a.md"));
    }

    [Fact]
    public async Task The_breadcrumb_starts_at_the_exposed_directory()
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/team/sub/c.md"));

        // Its start leads to team/sub, and nothing leads to team
        Assert.Matches("<a href=\"/team/sub\" title=\"sub\"", page);
        Assert.DoesNotContain("data-address=\"/team\"", page);
    }
}

/// <summary>What <c>Config:Exposed</c> may name: directories that are there, written as paths of the notes, that no address of the app takes.</summary>
public sealed class ExposedFolderSettingTests
{
    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("docs/a.md")]
    [InlineData("../outside")]
    [InlineData("_")]
    [InlineData("_content")]
    public void A_folder_that_can_not_be_shown_does_not_let_the_app_start(string exposed)
    {
        using NotesAppFactory app = new();
        BaseTree.Create(app.Tree);
        using var withSetting = app.WithWebHostBuilder(builder => builder.UseSetting("Config:Exposed:0", exposed));

        Exception refused = Assert.ThrowsAny<Exception>(() => withSetting.CreateClient());

        Assert.Contains("Config:Exposed", refused.ToString());
    }
}
