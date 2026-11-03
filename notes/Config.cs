namespace notes;

public sealed class Config
{
    public required string Dir { get; init; }
    public string Title { get; init; } = "Notes";
    /// <summary>Where the keys protecting the login session and antiforgery tokens are kept, so they survive restarts. Unset keeps the framework default.</summary>
    public string? KeysDir { get; init; }
    public required AuthConfig Auth { get; init; }
}

/// <summary>How the app signs users in with its Keycloak realm.</summary>
public sealed class AuthConfig
{
    /// <summary>Issuer URL of the realm, e.g. http://localhost:8080/realms/notes.</summary>
    public required string Authority { get; init; }
    /// <summary>Where to fetch the realm's discovery document from if that differs from the authority, e.g. an internal address that skips the public proxy. Allowed to be plain HTTP.</summary>
    public string? MetadataAddress { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
}
