#pragma warning disable CA1873
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using notes;
using notes.Features.Auth;
using notes.Features.Components.Create;
using notes.Features.Components.Delete;
using notes.Features.Components.Download;
using notes.Features.Components.Rename;
using notes.Features.Components.Selection;
using notes.Features.Components.Transfer;
using notes.Features.Components.Upload;
using notes.Features.Helpers;
using notes.Features.Permissions;
using notes.Features.View.Editor;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

Config config = builder.Configuration.GetSection("Config").Get<Config>()!;

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
// The one clock of the app (selection lifetime, stale uploads, session renewal, names of new entries), so that tests can replace it
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPathService, PathService>();
builder.Services.AddSingleton<IZipService, ZipService>();
builder.Services.AddSingleton<IUploadService, UploadService>();
// The selections live in memory of this process only: fine for a single instance, but a restart forgets them and several instances
// would not share them. For that, ISelectionService needs a shared store (e.g. a distributed cache).
builder.Services.AddMemoryCache();
// The cache expires entries (the selections) by the app's clock
builder.Services.AddOptions<MemoryCacheOptions>().Configure<TimeProvider>((options, time) => options.Clock = new TimeProviderClock(time));
// Permissions: what they mean and who checks them where is specified in docs/security-specification.md
builder.Services.AddSingleton<FolderConfigStore>();
builder.Services.AddSingleton<IEntryAccess, EntryAccess>();
builder.Services.AddSingleton<AccessTree>();
// One lock across the app (and so a single instance only) for everything that adds, removes or renames entries
builder.Services.AddSingleton<StructureLock>();
builder.Services.AddSingleton<ISelectionService, SelectionService>();
builder.Services.AddSingleton<TransferPlanner>();
builder.Services.AddSingleton<TransferExecutor>();
builder.Services.AddSingleton<ITransferService, TransferService>();
builder.Services.AddSingleton<SelectionHandler>();
builder.Services.AddSingleton<TransferHandler>();
builder.Services.AddSingleton<DownloadHandler>();
builder.Services.AddSingleton<RawHandler>();
builder.Services.AddSingleton<UploadHandler>();
builder.Services.AddSingleton<CreateHandler>();
builder.Services.AddSingleton<RenameHandler>();
builder.Services.AddSingleton<SaveHandler>();
builder.Services.AddSingleton<DeleteHandler>();
builder.Services.AddSingleton<ConfigHandler>();

// Background jobs. Their schedule is registered at every startup, so in-memory storage loses nothing that matters.
builder.Services.AddHangfire(options => options.UseInMemoryStorage());
builder.Services.AddHangfireServer();
builder.Services.AddScoped<ClearStaleUploadsJob>();

WebApplication app = builder.Build();
string dir = Path.GetFullPath(config.Dir);

if (!Directory.Exists(dir))
{
    _ = Directory.CreateDirectory(dir);
    app.Logger.LogInformation("Created directory: {dir}", dir);
}
app.Logger.LogInformation("In working dir: {dir}", dir);

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

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

app.Services.GetRequiredService<IRecurringJobManager>()
    .AddOrUpdate<ClearStaleUploadsJob>(ClearStaleUploadsJob.Id, job => job.Run(), Cron.Hourly);

app.Run();

/// <summary>Public so that the tests can start the app (WebApplicationFactory).</summary>
public partial class Program;