#pragma warning disable CA1873
using notes;
using notes.Pages.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

Config config = builder.Configuration.GetSection("Config").Get<Config>()!;

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IPathService, PathService>();

WebApplication app = builder.Build();
string dir = Path.GetFullPath(config.Dir);

if (!Directory.Exists(dir))
{
    _ = Directory.CreateDirectory(dir);
    app.Logger.LogInformation("Created directory: {dir}", dir);
}
app.Logger.LogInformation("In working dir: {dir}", dir);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();

app.Run();