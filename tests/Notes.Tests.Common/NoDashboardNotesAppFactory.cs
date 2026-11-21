using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Notes.Tests.Common;

/// <summary>The app as a site sets it up that leaves Hangfire's dashboard out (<see cref="NotesSetup.HangfireDashboard"/>).</summary>
public sealed class NoDashboardNotesAppFactory : NotesAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // What AddNotes was given, as the site's code would have chosen it (the endpoints and the layout ask it once the app is built)
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NotesSetup>();
            services.AddSingleton(new NotesSetup { HangfireDashboard = false });
        });
    }
}
