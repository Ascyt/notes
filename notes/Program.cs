#pragma warning disable CA1873
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using notes;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.Components;
using notes.Features.Components.Upload;
using notes.Features.Configuration;
using notes.Features.Editor;
using notes.Features.EntryActions;
using notes.Features.Helpers;
using notes.Features.History;
using notes.Extensions;
using notes.Features.HistoryViewing;
using notes.Features.ImageConversion;
using notes.Features.KillSwitch;
using notes.Features.Media;
using notes.Pages;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

Config config = builder.Configuration.GetSection("Config").Get<Config>()!;
// How large what users send may be (docs/development/user-input.md); limits that make no sense don't let the app start
config.Limits.Check();
ImageForBrowser.LimitPixels(config.Limits.MaxConvertedImagePixels);

// Add services to the container.
builder.Services.AddRazorPages();
// An explicit metadata address is the internal, plain-HTTP route to Keycloak
builder.Services.AddNotesAuth(config.Auth, requireHttpsMetadata: !builder.Environment.IsDevelopment() && config.Auth.MetadataAddress is null);
if (config.KeysDir is not null)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(config.KeysDir)).SetApplicationName("notes");

// The app is only reachable through the reverse proxy (the container publishes no port), so its forwarded headers can be trusted
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.Limits);
// The one clock of the app (selection lifetime, stale uploads, session renewal, names of new entries), so that tests can replace it
builder.Services.AddSingleton(TimeProvider.System);
// Where the notes are on disk: the root and the bridges (docs/administration/bridges.md). A configuration that isn't valid stops the app here.
builder.Services.AddSingleton(new NotesLocations(config));
builder.Services.AddSingleton<IPathService, PathService>();
builder.Services.AddMemoryCache();
// The cache expires entries (the selections) by the app's clock
builder.Services.AddOptions<MemoryCacheOptions>().Configure<TimeProvider>((options, time) => options.Clock = new TimeProviderClock(time));
// Permissions: what they mean and who checks them where is specified in docs/development/security-specification.md
builder.Services.AddSingleton<IEntryAccess, EntryAccess>();
builder.Services.AddSingleton<AccessTree>();
// One lock across the app (and so a single instance only) for everything that adds, removes or renames entries
builder.Services.AddSingleton<StructureLock>();
builder.Services.AddSingleton<KillSwitchService>();

// The features, each with the actions of the page of the entries that it answers (?download, ?rename=, …)
builder.Services.AddEntryActions();
builder.Services.AddConfiguration();
builder.Services.AddComponents();
builder.Services.AddEditor();
builder.Services.AddMedia();
builder.Services.AddHistory();
builder.Services.AddHistoryViewing();

// Background jobs. Their schedule is registered at every startup, so in-memory storage loses nothing that matters.
builder.Services.AddHangfire(options => options.UseInMemoryStorage());
builder.Services.AddHangfireServer();

WebApplication app = builder.Build();
// Two actions with the same parameter don't let the app start
_ = app.Services.GetRequiredService<EntryActions>();
string dir = Path.GetFullPath(config.Dir);

if (!Directory.Exists(dir))
{
    _ = Directory.CreateDirectory(dir);
    app.Logger.LogInformation("Created directory: {dir}", dir);
}
app.Logger.LogInformation("In working dir: {dir}", dir);

// The bridges need their directories on disk (and the app doesn't start without them), before anything uses the notes
NotesLocations locations = app.Services.GetRequiredService<NotesLocations>();
IPathService paths = app.Services.GetRequiredService<IPathService>();
locations.CheckOnDisk(paths.ToPhysicalPath, app.Logger);
foreach (NotesLocation bridge in locations.Bridges)
    app.Logger.LogInformation("Bridge: /{virtual} is in {physical}", bridge.VirtualPath, bridge.PhysicalPath);

// Git refuses a repository whose folders belong to another user, so that it never trusts one someone else put there. The history is the
// app's own, where the app is configured to keep it, and the notes folder is often mounted with an owner of its own (root, in Docker Desktop).
LibGit2Sharp.GlobalSettings.SetOwnerValidation(false);

// What changed while the app didn't run goes into the history on its own, before the migrations change anything
HistoryService history = app.Services.GetRequiredService<HistoryService>();
history.CommitPending(CommitReason.Startup);

// Writes what was migrated while reading back to the files, now that nothing else is using them
if (app.Services.GetRequiredService<FolderConfigStore>().MigrateAll() > 0)
    history.CommitPending(CommitReason.Migration);

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
app.UseSecurityHeaders();
// Once shut down, the app answers nothing but that
app.UseKillSwitch();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/_/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Empty error responses (404 and the like) get the error page. Only for requests a browser navigates to: the
// scripts of the pages just look at the status code.
app.UseWhen(
    context => HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method),
    branch => branch.UseStatusCodePagesWithReExecute("/_/Error"));

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseSessionNotice();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();

// Admins only. Going through the app's own policy (instead of Hangfire's filter) answers everyone else with the usual 404.
app.MapHangfireDashboard("/_/Hangfire", new DashboardOptions { Authorization = [] })
    .RequireAuthorization(NotesAccess.AdminPolicyName);

IRecurringJobManager recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<ClearStaleUploadsJob>(ClearStaleUploadsJob.Id, job => job.Run(), Cron.Hourly);
recurringJobs.AddOrUpdate<CommitIdleChangesJob>(CommitIdleChangesJob.Id, job => job.Run(), Cron.Minutely);

app.Run();

/// <summary>Public so that the tests can start the app (WebApplicationFactory).</summary>
public partial class Program;