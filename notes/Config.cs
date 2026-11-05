using notes.Features.Auth;

namespace notes;

public sealed class Config
{
    public required string Dir { get; init; }
    public string Title { get; init; } = "Notes";
    /// <summary>Where the keys protecting the login session and antiforgery tokens are kept, so they survive restarts. Unset keeps the framework default.</summary>
    public string? KeysDir { get; init; }
    public required AuthConfig Auth { get; init; }
}
