using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Notes.Tests.Integration;

/// <summary>
/// A site with a <c>_ViewStart</c> and a <c>_Layout</c> of its own (<see cref="SiteNotesAppFactory"/>): the notes stay in their own layout,
/// as long as the setup doesn't name another.
/// </summary>
public sealed class SiteViewsTests(SiteNotesAppFactory app) : IntegrationTest(app), IClassFixture<SiteNotesAppFactory>
{
    [Theory]
    [InlineData("/docs")]
    [InlineData("/docs/a.md")]
    [InlineData("/_/About")]
    [InlineData("/does/not/exist")]
    public async Task The_pages_of_the_notes_keep_their_own_layout(string url)
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, url));

        Assert.DoesNotContain("id=\"site-own-layout\"", page);
        Assert.Contains("id=\"navbar-menu\"", page);
    }
}

/// <summary>A site that shows the notes in a layout it made for them (<see cref="NotesSetup.Layout"/>, docs/library.md).</summary>
public sealed class SiteLayoutTests(SiteLayoutNotesAppFactory app) : IntegrationTest(app), IClassFixture<SiteLayoutNotesAppFactory>
{
    [Theory]
    [InlineData("/docs")]
    [InlineData("/docs/a.md")]
    [InlineData("/_/About")]
    [InlineData("/does/not/exist")]
    public async Task The_pages_of_the_notes_are_in_the_layout_of_the_site_with_what_they_need(string url)
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, url));

        Assert.Contains("data-site-layout", page);
        Assert.DoesNotContain("id=\"navbar-menu\"", page);
        // The parts of the notes: the head, the stack of messages, the scripts, and what the site chose of the menu
        Assert.Contains("name=\"app-root-names\"", page);
        Assert.Contains("id=\"floating-cards\"", page);
        // (The static assets are served under a name with their fingerprint in it)
        Assert.Matches("<script src=\"/_content/Notes/Features/Shortcuts/shortcuts[^\"]*\\.js", page);
        Assert.Contains("href=\"/_/About\"", page);
    }
}

/// <summary>Links of the site before the start of the breadcrumb (<c>Config:BreadcrumbParents</c>, docs/library.md).</summary>
public sealed class BreadcrumbParentTests(BreadcrumbParentsNotesAppFactory app) : IntegrationTest(app), IClassFixture<BreadcrumbParentsNotesAppFactory>
{
    [Theory]
    [InlineData("/docs/a.md")]
    [InlineData("/")]
    public async Task They_come_before_the_start_in_their_order_and_lead_out_of_the_notes(string url)
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, url));

        int first = page.IndexOf($"href=\"{BreadcrumbParentsNotesAppFactory.SiteStart}\" data-outside-app", StringComparison.Ordinal);
        int second = page.IndexOf($"href=\"{BreadcrumbParentsNotesAppFactory.Elsewhere}\" data-outside-app", StringComparison.Ordinal);
        int start = page.IndexOf("fa-house", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < second && second < start, "in order, before the start");
    }
}

/// <summary>What <c>Config:BreadcrumbParents</c> may name.</summary>
public sealed class BreadcrumbParentSettingTests
{

    [Theory]
    [InlineData("", "/")]
    [InlineData("No address", "")]
    [InlineData("Another host", "//example.org")]
    [InlineData("A script", "javascript:alert(1)")]
    [InlineData("Relative", "projects")]
    public void One_that_is_none_does_not_let_the_app_start(string name, string address)
    {
        using NotesAppFactory app = new();
        using var withSetting = app.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Config:BreadcrumbParents:0:Name", name);
            builder.UseSetting("Config:BreadcrumbParents:0:Address", address);
        });

        Exception refused = Assert.ThrowsAny<Exception>(() => withSetting.CreateClient());

        Assert.Contains("Config:BreadcrumbParents", refused.ToString());
    }
}
