using Microsoft.AspNetCore.Hosting;

namespace Notes.Tests.Common;

/// <summary>
/// The app, but a request never waits for an archived folder to be unzipped (<c>Config:Archives:WaitSeconds</c> 0): it is answered with how
/// far it got at once, as it is for a folder that takes long, so that what the pages do then can be seen with a small one.
/// </summary>
public sealed class NoWaitForUnzippingNotesAppFactory : NotesAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Config:Archives:WaitSeconds", "0");
    }
}
