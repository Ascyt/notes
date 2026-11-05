#pragma warning disable CA1873
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using notes;
using notes.Features.Auth;
using notes.Features.Components.Create;
using notes.Features.Components.Delete;
using notes.Features.Components.Download;
using notes.Features.Components.Rename;
using notes.Features.Components.Upload;
using notes.Features.Helpers;
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
builder.Services.AddSingleton<IPathService, PathService>();
builder.Services.AddSingleton<IZipService, ZipService>();
builder.Services.AddSingleton<IUploadService, UploadService>();
builder.Services.AddSingleton<DownloadHandler>();
builder.Services.AddSingleton<UploadHandler>();
builder.Services.AddSingleton<CreateHandler>();
builder.Services.AddSingleton<RenameHandler>();
builder.Services.AddSingleton<SaveHandler>();
builder.Services.AddSingleton<DeleteHandler>();

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
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();

// Admins only. Going through the app's own policy (instead of Hangfire's filter) answers everyone else with the usual 404.
app.MapHangfireDashboard("/_/Hangfire", new DashboardOptions { Authorization = [] })
    .RequireAuthorization(NotesAccess.PolicyName);

app.Services.GetRequiredService<IRecurringJobManager>()
    .AddOrUpdate<ClearStaleUploadsJob>(ClearStaleUploadsJob.Id, job => job.Run(), Cron.Hourly);

app.Run();