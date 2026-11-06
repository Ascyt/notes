using System.Net;

namespace notes.Tests.Integration;

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
    public async Task Admins_get_the_dashboard()
        => await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Admin, "/_/Hangfire"));
}
