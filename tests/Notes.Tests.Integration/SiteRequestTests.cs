using System.Net;
using Notes.Features.KillSwitch;

namespace Notes.Tests.Integration;

/// <summary>
/// What the app adds to its answers (security headers, the kill switch, its error page) is for its own requests only: a page of the site
/// (<see cref="SiteNotesAppFactory"/>) stays as the site makes it, also where the app shows all of the entries.
/// </summary>
public sealed class SiteRequestTests(SiteNotesAppFactory app) : IntegrationTest(app), IClassFixture<SiteNotesAppFactory>
{
    private const string SitePage = "/site-page";

    [Fact]
    public async Task Only_the_answers_of_the_app_get_its_security_headers()
    {
        HttpResponseMessage ofTheApp = await Client.GetAsync(Users.Alice, "/docs");
        HttpResponseMessage ofTheSite = await Client.GetAsync(Users.Alice, SitePage);

        await AssertStatus(HttpStatusCode.OK, ofTheSite);
        Assert.Contains("id=\"site-page\"", await BodyOf(ofTheSite));
        Assert.True(ofTheApp.Headers.Contains("Content-Security-Policy"));
        Assert.False(ofTheSite.Headers.Contains("Content-Security-Policy"));
        Assert.False(ofTheSite.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public async Task A_missing_address_of_the_app_gets_its_error_page()
    {
        foreach (string url in new[] { "/docs/missing.md", "/_/Missing" })
        {
            HttpResponseMessage missing = await Client.GetAsync(Users.Alice, url);
            await AssertStatus(HttpStatusCode.NotFound, missing);
            Assert.Contains("id=\"navbar-menu\"", await BodyOf(missing));
        }
    }
}

/// <summary>Where the app shows only some directories, every other address is the site's, with its own answer to what is missing.</summary>
public sealed class SiteAddressTests(ExposedNotesAppFactory app) : IntegrationTest(app), IClassFixture<ExposedNotesAppFactory>
{
    protected override string SelectionAddress => "/docs";

    [Theory]
    [InlineData("/")]
    [InlineData("/team")]
    [InlineData("/not/there")]
    public async Task What_is_not_exposed_is_left_to_the_site(string url)
    {
        HttpResponseMessage answer = await Client.GetAsync(Users.Admin, url);

        await AssertStatus(HttpStatusCode.NotFound, answer);
        // The site has no error page here, so the answer stays empty: nothing of the app's
        Assert.Equal("", await BodyOf(answer));
        Assert.False(answer.Headers.Contains("Content-Security-Policy"));
    }
}

/// <summary>A site whose app was shut down before it started (<see cref="KillSwitchRestartTests"/>): the site's own pages stay up.</summary>
public sealed class SiteKillSwitchTests
{
    [Fact]
    public async Task The_pages_of_the_site_stay_up()
    {
        using SiteNotesAppFactory site = new();
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(site.KillSwitchMarker, "Shut down by a test", cancellation);
        NotesClient client = new(site);

        HttpResponseMessage ofTheApp = await client.GetAsync(Users.Admin, "/docs");
        HttpResponseMessage ofTheSite = await client.GetAsync(Users.Admin, "/site-page");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ofTheApp.StatusCode);
        Assert.Equal(KillSwitchService.ShutdownPage(new KillSwitchTexts()), await ofTheApp.Content.ReadAsStringAsync(cancellation));
        Assert.Equal(HttpStatusCode.OK, ofTheSite.StatusCode);
    }
}
