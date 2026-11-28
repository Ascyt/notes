#pragma warning disable CA1873
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Extensions;
using Notes.Features.Addresses;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Components;
using Notes.Features.Components.Archive;
using Notes.Features.Components.Breadcrumb;
using Notes.Features.Components.Upload;
using Notes.Features.Configuration;
using Notes.Features.Editor;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.History;
using Notes.Features.HistoryViewing;
using Notes.Features.ImageConversion;
using Notes.Features.KillSwitch;
using Notes.Features.Markdown;
using Notes.Features.Media;
using Notes.Features.Passwords;
using Notes.Features.ZippedFolders;
using Notes.Pages.App;
using Notes.Pages.Browse;
using Notes.Pages.Shared.AppLayout;

namespace Notes;

/// <summary>
/// How a site uses the entries (docs/library.md): <see cref="AddNotes"/> with the services, then, once the app is built, <see cref="UseNotes"/>
/// in its pipeline and <see cref="MapNotes"/> with its endpoints.
/// </summary>
public static class NotesHosting
{
    /// <summary>
    /// The services of the app, with their settings read from the configuration (<see cref="NotesSetup.ConfigSection"/>). Settings that make
    /// no sense don't let the app start.
    /// </summary>
    public static WebApplicationBuilder AddNotes(this WebApplicationBuilder builder, Action<NotesSetup>? configure = null)
    {
        NotesSetup setup = new();
        configure?.Invoke(setup);
        IServiceCollection services = builder.Services;
        _ = services.AddSingleton(setup);

        Config config = builder.Configuration.GetSection(setup.ConfigSection).Get<Config>()
            ?? throw new InvalidOperationException($"The settings of the app (the section {setup.ConfigSection} of the configuration) are missing.");
        // How large what users send may be (docs/development/user-input.md); limits that make no sense don't let the app start
        config.Limits.Check();
        ImageForBrowser.LimitPixels(config.Limits.MaxConvertedImagePixels);
        _ = services.AddSingleton(config);
        _ = services.AddSingleton(config.Limits);

        // What of the site's addresses the entries take: the pages of the app where the settings put them, and the directories that are shown
        AppAddresses appAddresses = new(config);
        _ = services.AddSingleton(appAddresses);
        _ = services.AddSingleton<ExposedFolders>();
        _ = services.AddSingleton<BreadcrumbStarts>();

        _ = services.AddSingleton<AppRequests>();
        // The page of the entries answers only the paths the site shows, leaving every other address to the site
        _ = services.Configure<RouteOptions>(options => options.SetParameterPolicy<ExposedPathConstraint>(ExposedPathConstraint.Name));

        _ = services.AddRazorPages(options =>
        {
            // (Messages are plain text, whatever the request asks for)
            options.Conventions.ConfigureFilter(new PlainTextMessages());
            options.Conventions.AddFolderRouteModelConvention("/App", page => MoveToAppPath(page, appAddresses));
            // The app's own pages, told from the site's by where their code is (a site may replace the view of one, but not its model)
            options.Conventions.AddFolderApplicationModelConvention("/", page =>
            {
                if (page.HandlerType.Assembly == typeof(NotesHosting).Assembly)
                    page.EndpointMetadata.Add(AppEndpoint.Instance);
            });
        });
        // An explicit metadata address is the internal, plain-HTTP route to Keycloak
        _ = services.AddAppAuth(config.Auth, requireHttpsMetadata: !builder.Environment.IsDevelopment() && config.Auth.MetadataAddress is null);
        if (config.KeysDir is not null)
            _ = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(config.KeysDir)).SetApplicationName("notes");

        // The one clock of the app (selection lifetime, stale uploads, session renewal, names of new entries), so that tests can replace it
        services.TryAddSingleton(TimeProvider.System);
        // Where the entries are on disk: the root and the bridges (docs/administration/bridges.md). A configuration that isn't valid stops the
        // app here.
        _ = services.AddSingleton(new EntryLocations(config));
        _ = services.AddSingleton<IPathService, PathService>();
        // Why a name or path can't be that of a new entry, the site's to replace (see NameTexts)
        services.TryAddSingleton<NameTexts>();
        _ = services.AddMemoryCache();
        // The cache expires entries (the selections) by the app's clock
        _ = services.AddOptions<MemoryCacheOptions>().Configure<TimeProvider>((options, time) => options.Clock = new TimeProviderClock(time));
        // Permissions: what they mean and who checks them where is specified in docs/development/security-specification.md
        _ = services.AddSingleton<IEntryAccess, EntryAccess>();
        _ = services.AddSingleton<AccessTree>();
        // One lock across the app (and so a single instance only) for everything that adds, removes or renames entries
        _ = services.AddSingleton<StructureLock>();
        // What a request looked up on disk, so that it looks each up once (principles.md, 22)
        _ = services.AddHttpContextAccessor();
        _ = services.AddSingleton<RequestLookups>();
        _ = services.AddSingleton<KillSwitchService>();
        services.TryAddSingleton<KillSwitchTexts>();
        services.TryAddSingleton<AuthTexts>();
        // The texts of the layout and of the pages of the app, the site's to replace (see LayoutTexts)
        services.TryAddSingleton<LayoutTexts>();
        services.TryAddSingleton<AppPageTexts>();

        // The features, each with the actions of the page of the entries that it answers (?download, ?rename=, …)
        _ = services.AddEntryActions();
        _ = services.AddPasswords(config.Passwords);
        _ = services.AddConfiguration();
        _ = services.AddComponents();
        _ = services.AddListing();
        _ = services.AddEditor();
        _ = services.AddMarkdown();
        _ = services.AddMedia();
        _ = services.AddHistory();
        _ = services.AddHistoryViewing();
        _ = services.AddArchivedFolders(config.Archives);
        // (Does again what the start does, across the features above)
        _ = services.AddScoped<Features.Maintenance.SoftRestartJob>();

        // The background jobs (scheduled in UseNotes)
        _ = services.AddHangfire(setup.ConfigureHangfire);
        _ = services.AddHangfireServer();
        return builder;
    }

    /// <summary>
    /// Gets the entries ready (their folder, the bridges, the history, the migrations of the configurations), schedules the background jobs, and
    /// adds what the requests of the app go through: the security headers, the kill switch, the error page, routing and the login. It comes
    /// where the pipeline would have its routing: after what the site does with every request first (forwarded headers, HTTPS, the
    /// exception handler).
    /// </summary>
    public static WebApplication UseNotes(this WebApplication app)
    {
        // Two actions with the same parameter don't let the app start, nor do links of the breadcrumb that are none
        _ = app.Services.GetRequiredService<EntryActions>();
        _ = app.Services.GetRequiredService<BreadcrumbStarts>();
        PrepareTheApp(app);
        ScheduleJobs(app);

        // Empty error responses (404 and the like) get the error page. Only for requests a browser navigates to: the
        // scripts of the pages just look at the status code. Before routing, so that the error page is routed again.
        string errorPage = app.Services.GetRequiredService<AppAddresses>().ErrorPage;
        _ = app.UseWhen(
            context => HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method),
            branch => branch.UseStatusCodePagesWithReExecute(errorPage));

        _ = app.UseRouting();

        // What the app adds to its answers is for its own requests only: the site's pages keep their own headers and error pages, and stay up
        // when the kill switch is pulled
        AppRequests requests = app.Services.GetRequiredService<AppRequests>();
        _ = app.UseWhen(requests.IsForTheApp, branch => branch.UseSecurityHeaders().UseKillSwitch());
        _ = app.UseWhen(context => !requests.IsForTheApp(context), branch => branch.Use(LeaveErrorPagesToTheSite));

        _ = app.UseAuthentication();
        _ = app.UseWhen(requests.IsForTheApp, branch => branch.UseSessionNotice());
        _ = app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// The pages of the app (every address of the entries, and the app's own under /_/), and Hangfire's dashboard if the setup has it. The
    /// scripts and styles are among the site's static assets (<c>app.MapStaticAssets()</c>, which the site maps itself).
    /// </summary>
    public static WebApplication MapNotes(this WebApplication app)
    {
        _ = app.MapRazorPages().WithStaticAssets();

        // Admins only. Going through the app's own policy (instead of Hangfire's filter) answers everyone else with the usual 404.
        if (app.Services.GetRequiredService<NotesSetup>().HangfireDashboard)
            _ = app.MapHangfireDashboard(app.Services.GetRequiredService<AppAddresses>().Of("Hangfire"), new DashboardOptions { Authorization = [] })
                .RequireAuthorization(AppAccess.AdminPolicyName)
                .WithMetadata(AppEndpoint.Instance);
        return app;
    }

    /// <summary>The page of the app to show errors on (for the site's exception handler), at the address the settings give the app's pages.</summary>
    public static string NotesErrorPage(this WebApplication app)
        => app.Services.GetRequiredService<AppAddresses>().ErrorPage;

    /// <summary>
    /// Puts a page of the app (Pages/App, whose <c>@page</c> gives its address under the default <c>/_</c>) under the address of the settings
    /// (<see cref="Config.AppPath"/>). Literal addresses go before the page of the app (<c>{**path}</c>), so the page wins over a directory
    /// of the same name.
    /// </summary>
    private static void MoveToAppPath(PageRouteModel page, AppAddresses appAddresses)
    {
        foreach (SelectorModel selector in page.Selectors)
        {
            if (selector.AttributeRouteModel is { Template: { } template } route && template.TrimStart('/').StartsWith("_/",
                StringComparison.Ordinal))
                route.Template = appAddresses.Of(template.TrimStart('/')["_/".Length..]);
        }
    }

    private static Task LeaveErrorPagesToTheSite(HttpContext context, RequestDelegate next)
    {
        if (context.Features.Get<IStatusCodePagesFeature>() is { } errorPages)
            errorPages.Enabled = false;
        return next(context);
    }

    /// <summary>What has to be there, and done, before the first request.</summary>
    private static void PrepareTheApp(WebApplication app)
    {
        string dir = Path.GetFullPath(app.Services.GetRequiredService<Config>().Dir);
        if (!Directory.Exists(dir))
        {
            _ = Directory.CreateDirectory(dir);
            app.Logger.LogInformation("Created directory: {dir}", dir);
        }
        app.Logger.LogInformation("In working dir: {dir}", dir);

        // The bridges need their directories on disk (and the app doesn't start without them), before anything uses the entries
        EntryLocations locations = app.Services.GetRequiredService<EntryLocations>();
        IPathService paths = app.Services.GetRequiredService<IPathService>();
        locations.CheckOnDisk(paths.ToPhysicalPath, app.Logger);
        foreach (EntryLocation bridge in locations.Bridges)
            app.Logger.LogInformation("Bridge: /{virtual} is in {physical}", bridge.VirtualPath, bridge.PhysicalPath);
        // So do the directories that are shown (after the bridges, which some of them may be in)
        ExposedFolders exposed = app.Services.GetRequiredService<ExposedFolders>();
        exposed.CheckOnDisk(paths, locations);

        // Git refuses a repository whose folders belong to another user, so that it never trusts one someone else put there. The history is the
        // app's own, where the app is configured to keep it, and the root folder is often mounted with an owner of its own (root, in Docker Desktop).
        LibGit2Sharp.GlobalSettings.SetOwnerValidation(false);

        // What changed while the app didn't run goes into the history on its own, before the migrations change anything
        // (Each step is logged with how long it took: a big folder can make them slow, and a start that goes quiet says nothing otherwise)
        HistoryService history = app.Services.GetRequiredService<HistoryService>();
        StartupSteps.Run(app.Logger, "committing what changed while the app didn't run to the history",
            () => history.CommitPending(CommitReason.Startup));

        // Writes what was migrated while reading back to the files, now that nothing else is using them
        int migrated = 0;
        StartupSteps.Run(app.Logger, "looking through the configuration files and migrating them",
            () => migrated = app.Services.GetRequiredService<FolderConfigStore>().MigrateAll());
        if (migrated > 0)
            StartupSteps.Run(app.Logger, "committing the migrated configuration files to the history",
                () => history.CommitPending(CommitReason.Migration));

        // What zipping or unzipping an archived folder left when the app stopped is put right (nothing of it is lost), before it is used
        StartupSteps.Run(app.Logger, "putting right what zipping or unzipping archived folders left",
            () => app.Services.GetRequiredService<ArchivedFolders>().RecoverAsync().GetAwaiter().GetResult());
        // What unzipping an uploaded archive left is deleted: the listing starts it over (the archive's entry still says it is to be unzipped)
        StartupSteps.Run(app.Logger, "deleting what unzipping uploaded archives left",
            () => app.Services.GetRequiredService<Features.Components.Upload.UploadedArchiveUnzipper>().ClearLeftovers());
    }

    /// <summary>
    /// The background jobs, on the schedules of the settings (<see cref="JobsConfig"/>). They are scheduled at every start, so in-memory storage
    /// loses nothing that matters. A schedule that isn't one stops the start here.
    /// </summary>
    private static void ScheduleJobs(WebApplication app)
    {
        JobsConfig jobs = app.Services.GetRequiredService<Config>().Jobs;
        IRecurringJobManager recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        Schedule(nameof(JobsConfig.ClearStaleUploads), jobs.ClearStaleUploads,
            schedule => recurringJobs.AddOrUpdate<ClearStaleUploadsJob>(ClearStaleUploadsJob.Id, job => job.Run(), schedule));
        Schedule(nameof(JobsConfig.ClearStaleArchives), jobs.ClearStaleArchives,
            schedule => recurringJobs.AddOrUpdate<ClearStaleArchivesJob>(ClearStaleArchivesJob.Id, job => job.Run(), schedule));
        Schedule(nameof(JobsConfig.CommitIdleChanges), jobs.CommitIdleChanges,
            schedule => recurringJobs.AddOrUpdate<CommitIdleChangesJob>(CommitIdleChangesJob.Id, job => job.Run(), schedule));
        Schedule(nameof(JobsConfig.ZipIdleArchives), jobs.ZipIdleArchives,
            schedule => recurringJobs.AddOrUpdate<ZipIdleArchivesJob>(ZipIdleArchivesJob.Id, job => job.RunAsync(), schedule));
        Schedule(nameof(JobsConfig.RescanConfigs), jobs.RescanConfigs,
            schedule => recurringJobs.AddOrUpdate<Features.Configuration.RescanConfigsJob>(Features.Configuration.RescanConfigsJob.Id,
                job => job.Run(), schedule));
        Schedule(nameof(JobsConfig.SoftRestart), jobs.SoftRestart,
            schedule => recurringJobs.AddOrUpdate<Features.Maintenance.SoftRestartJob>(Features.Maintenance.SoftRestartJob.Id,
                job => job.RunAsync(), schedule));
    }

    // Hangfire refuses what isn't a cron expression with an ArgumentException that doesn't say which setting it was
    private static void Schedule(string setting, string schedule, Action<string> addOrUpdate)
    {
        try
        {
            addOrUpdate(schedule);
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException($"Config:Jobs:{setting} ('{schedule}') is not a cron expression: {e.Message}", e);
        }
    }
}
