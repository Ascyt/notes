using System.Net;

namespace notes.Tests.Integration;

/// <summary>Who may pull the kill switch. Nobody does here, so the app stays up for the other tests of the class.</summary>
public sealed class KillSwitchAccessTests(NotesAppFactory app) : IntegrationTest(app)
{
    [Fact]
    public async Task Only_admins_get_the_kill_switch()
    {
        Assert.DoesNotContain("kill-switch-modal", await BodyOf(await Client.GetAsync(Users.Alice, "/")));
        Assert.Contains("kill-switch-modal", await BodyOf(await Client.GetAsync(Users.Admin, "/")));
    }

    [Fact]
    public async Task Users_who_are_not_admins_cannot_pull_it()
    {
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Alice, "/_/Shutdown"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.PostAsync(Users.Nobody, "/_/Shutdown"));
        Assert.False(File.Exists(App.KillSwitchMarker));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/"));
    }

    [Fact]
    public async Task It_is_not_pulled_without_the_antiforgery_token()
    {
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Admin, "/_/Shutdown", antiforgery: false));
        Assert.False(File.Exists(App.KillSwitchMarker));
    }
}

/// <summary>Pulling the kill switch, with an app of its own, as it shuts that app down. The test app has no Keycloak to disable the realm of.</summary>
public sealed class KillSwitchShutdownTests(NotesAppFactory app) : IntegrationTest(app)
{
    [Fact]
    public async Task Once_pulled_everyone_only_gets_a_vague_shutdown_page()
    {
        HttpResponseMessage pulled = await Client.PostAsync(Users.Admin, "/_/Shutdown");
        await AssertStatus(HttpStatusCode.Redirect, pulled);
        Assert.Equal("/", pulled.Headers.Location?.ToString());
        Assert.True(File.Exists(App.KillSwitchMarker));

        foreach (TestUser user in new[] { Users.Admin, Users.Alice, Users.Anonymous })
        {
            HttpResponseMessage page = await Client.GetAsync(user, "/");
            await AssertStatus(HttpStatusCode.ServiceUnavailable, page);
            string body = await BodyOf(page);
            Assert.Contains("This site has been shut down.", body);
            // Nothing of why, or of the server
            Assert.DoesNotContain("kill", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Keycloak", body);
            Assert.DoesNotContain(App.KillSwitchMarker, body);
        }
    }
}

/// <summary>An app that starts with the marker of a kill switch pulled before (Docker starts the container again).</summary>
public sealed class KillSwitchRestartTests
{
    [Fact]
    public async Task After_a_restart_the_site_only_answers_that_it_is_shut_down()
    {
        using NotesAppFactory restarted = new();
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(restarted.KillSwitchMarker, "Shut down by a test", cancellation);
        NotesClient client = new(restarted);

        foreach (string url in new[] { "/", "/_/Login", "/css/site.css" })
        {
            HttpResponseMessage page = await client.GetAsync(Users.Admin, url);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, page.StatusCode);
            Assert.Contains("This site has been shut down.", await page.Content.ReadAsStringAsync(cancellation));
        }
    }
}
