using System.Net;

namespace Notes.Tests.Integration;

/// <summary>
/// Hangfire keeps its storage in a static (one per process), which every app of the parallel tests replaces with its own. The dashboard of one
/// app therefore only works while no other app runs.
/// </summary>
[CollectionDefinition(nameof(HangfireDashboardTests), DisableParallelization = true)]
public sealed class HangfireCollection;

[Collection(nameof(HangfireDashboardTests))]
public sealed class HangfireDashboardTests(NotesAppFactory app) : IntegrationTest(app)
{
    [Fact]
    public async Task Admins_get_the_dashboard_and_the_way_to_it()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Admin, "/_/Hangfire"));
        Assert.Contains("href=\"/_/Hangfire\"", await BodyOf(await Client.GetAsync(Users.Admin, "/")));
    }
}

/// <summary>A site that leaves the dashboard out (<see cref="NotesSetup.HangfireDashboard"/>): it isn't there, nor is the way to it.</summary>
public sealed class NoHangfireDashboardTests(NoDashboardNotesAppFactory app) : IntegrationTest(app), IClassFixture<NoDashboardNotesAppFactory>
{
    [Fact]
    public async Task There_is_no_dashboard_even_for_admins()
    {
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/_/Hangfire"));
        Assert.DoesNotContain("/_/Hangfire", await BodyOf(await Client.GetAsync(Users.Admin, "/")));
    }
}
