using Microsoft.AspNetCore.HttpOverrides;
using Notes;

// The site of the notes as it is deployed (docs/administration/deployment.md): nothing but the notes, with their defaults, behind a
// reverse proxy. How another site uses them is described in docs/library.md.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddNotes();

// The app is only reachable through the reverse proxy (the container publishes no port), so its forwarded headers can be trusted
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

WebApplication app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    // The error page of the notes, which says nothing about how the site is run (docs/development/principles.md, 4)
    app.UseExceptionHandler(app.NotesErrorPage());
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseNotes();

app.MapStaticAssets();
app.MapNotes();

app.Run();
