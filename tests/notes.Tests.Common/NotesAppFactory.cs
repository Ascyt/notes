using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using notes.Features.Auth;

namespace notes.Tests.Common;

/// <summary>
/// The whole app, as it runs in production (environment "Testing": the production pipeline, with its error pages), on a notes directory of
/// its own and with a clock the tests control. Users are signed in by <see cref="TestAuthentication"/> instead of Keycloak, and the
/// background jobs don't run by themselves (a test runs a job when it wants to).
/// </summary>
public class NotesAppFactory : WebApplicationFactory<Program>
{
    public NotesAppFactory()
    {
        string id = Guid.NewGuid().ToString("N");
        NotesDir = Path.Combine(Path.GetTempPath(), "notes-tests", id);
        _ = Directory.CreateDirectory(NotesDir);
        KillSwitchMarker = Path.Combine(Path.GetTempPath(), "notes-tests", id + ".killed");
    }

    /// <summary>Where the kill switch of this app leaves its marker (by default it would be in the project's directory).</summary>
    public string KillSwitchMarker { get; }

    /// <summary>The notes directory of this app (physical).</summary>
    public string NotesDir { get; }

    /// <summary>The app's clock. Starts at the real time (the files on disk have real times), and only moves when a test moves it.</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public NotesTree Tree => new(NotesDir);

    /// <summary>Called (on the thread of the request) every time the app asks what a user may do with a physical path, before it is answered.</summary>
    public Action<ClaimsPrincipal, string>? BeforeAccessCheck { get; set; }

    private sealed class ObservedEntryAccess(EntryAccess inner, NotesAppFactory app) : IEntryAccess
    {
        public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath)
        {
            app.BeforeAccessCheck?.Invoke(user, physicalPath);
            return inner.GetAccess(user, physicalPath);
        }

        // Passed on as it is: the app asks this way for what it has just listed, and must not look at the disk again here either
        public EntryAccessInfo GetAccess(ClaimsPrincipal user, string physicalPath, bool isDirectory)
        {
            app.BeforeAccessCheck?.Invoke(user, physicalPath);
            return inner.GetAccess(user, physicalPath, isDirectory);
        }

        public bool CanManagePermissions(ClaimsPrincipal user) => inner.CanManagePermissions(user);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // The scripts and styles from the project (as in Development), as the tests don't run on a published app
        builder.UseStaticWebAssets();
        // Settings (not ConfigureAppConfiguration), as Program reads the configuration before the app is built
        builder.UseSetting("Config:Dir", NotesDir);
        builder.UseSetting("Config:Title", "Notes");
        builder.UseSetting("Config:KillSwitchMarker", KillSwitchMarker);
        builder.UseSetting("Config:Auth:Authority", "https://auth.invalid/realms/notes");
        builder.UseSetting("Config:Auth:ClientId", "notes-web");
        builder.UseSetting("Config:Auth:ClientSecret", "test-secret");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IStartupFilter, TestAuthentication>();
            // The app's own permissions, with a way for a test to step in whenever they are asked (to pause an operation half way)
            services.RemoveAll<IEntryAccess>();
            services.AddSingleton<EntryAccess>();
            services.AddSingleton<IEntryAccess>(provider => new ObservedEntryAccess(provider.GetRequiredService<EntryAccess>(), this));

            // Hangfire's server would run the stale-uploads job by the real clock, whenever the hour is full
            foreach (ServiceDescriptor hangfireServer in services.Where(IsHangfireServer).ToList())
                _ = services.Remove(hangfireServer);
        });
    }

    private static bool IsHangfireServer(ServiceDescriptor descriptor)
        => descriptor.ServiceType == typeof(IHostedService)
           && (descriptor.ImplementationType ?? descriptor.ImplementationFactory?.Method.DeclaringType ?? descriptor.ImplementationInstance?.GetType())
           ?.Assembly.GetName().Name?.StartsWith("Hangfire", StringComparison.Ordinal) == true;

    /// <summary>Empties the notes directory (also the temp files of uploads and transfers).</summary>
    public void ResetNotes()
    {
        foreach (string directory in Directory.EnumerateDirectories(NotesDir))
            Directory.Delete(directory, recursive: true);
        foreach (string file in Directory.EnumerateFiles(NotesDir))
            File.Delete(file);
    }

    /// <summary>
    /// What a page would give a script of the user to send along with a POST or DELETE: the antiforgery cookie and the request token.
    /// Made directly by the app's antiforgery service, so that also users without any page they may open can be tested.
    /// </summary>
    public AntiforgeryTokens TokensFor(TestUser user)
    {
        using IServiceScope scope = Services.CreateScope();
        DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider, User = user.ToPrincipal() };
        AntiforgeryTokenSet set = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        string cookieName = scope.ServiceProvider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name!;
        return new AntiforgeryTokens(cookieName, set.CookieToken!, set.HeaderName!, set.RequestToken!);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(NotesDir, recursive: true);
            File.Delete(KillSwitchMarker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file still open; it is in the temp folder anyway
        }
    }
}

public sealed record AntiforgeryTokens(string CookieName, string CookieToken, string HeaderName, string RequestToken)
{
    public string Cookie => $"{CookieName}={CookieToken}";
}
