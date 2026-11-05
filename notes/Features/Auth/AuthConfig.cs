namespace notes.Features.Auth;

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
