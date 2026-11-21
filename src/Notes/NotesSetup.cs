using Hangfire;

namespace Notes;

/// <summary>
/// How a site uses the entries, chosen in its code (<see cref="NotesHosting.AddNotes"/>). What whoever runs the site sets (the root folder,
/// the login, the limits) is in the settings instead (<see cref="Config"/>, the section <see cref="ConfigSection"/>).
/// </summary>
public sealed class NotesSetup
{
    /// <summary>The section of the site's configuration (appsettings.json, the environment) with the settings of the app.</summary>
    public string ConfigSection { get; set; } = "Config";

    /// <summary>
    /// The layout the pages of the app are shown in, by its full path (<c>/Pages/Shared/_Layout.cshtml</c>), for a site with a layout of its
    /// own. It has the parts every layout of the app needs (docs/library.md). By default the app's own, of a site that is nothing but them.
    /// </summary>
    public string Layout { get; set; } = "/Pages/Shared/AppLayout/_Layout.cshtml";

    /// <summary>
    /// Where Hangfire keeps the background jobs. In memory by default: the jobs are scheduled again at every start, so nothing that matters is
    /// lost. A site with a database for Hangfire can keep them there.
    /// </summary>
    public Action<IGlobalConfiguration> ConfigureHangfire { get; set; } = hangfire => hangfire.UseInMemoryStorage();

    /// <summary>
    /// Whether the site has Hangfire's dashboard, for admins only, among the pages of the app (<c>/_/Hangfire</c>, see <see cref="Config.AppPath"/>).
    /// Without it there is no link to it either. When the jobs run is a setting (<see cref="JobsConfig"/>).
    /// </summary>
    public bool HangfireDashboard { get; set; } = true;
}
