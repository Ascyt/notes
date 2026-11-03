#pragma warning disable CA1873
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using notes;
using notes.Auth;
using notes.Pages.Services;

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
builder.Services.AddSingleton<IFileService, FileService>();
builder.Services.AddSingleton<IDirectoryService, DirectoryService>();

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
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();

app.Run();