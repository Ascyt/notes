using notes.Features.Auth;

namespace notes;

public sealed class Config
{
    public required string Dir { get; init; }
    /// <summary>
    /// The version of the app (major.minor.patch). Every <c>.config.json</c> is brought up to it when it is read, which sets its <c>version</c>
    /// and applies the migrations in <see cref="notes.Features.Permissions.Migrations.ConfigMigrations"/> after the version it had. A version
    /// that changes nothing in the files needs no migration.
    /// </summary>
    public required string Version { get; init; }
    public string Title { get; init; } = "Notes";
    /// <summary>Where the keys protecting the login session and antiforgery tokens are kept, so they survive restarts.</summary>
    public string? KeysDir { get; init; }
    /// <summary>The file that keeps the site shut down once the kill switch was pulled. By default "killed" in <see cref="KeysDir"/> (or the app's directory).</summary>
    public string? KillSwitchMarker { get; init; }
    public required AuthConfig Auth { get; init; }
}
