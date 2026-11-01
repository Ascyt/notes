namespace notes;

public sealed class Config
{
    public required string Dir { get; init; }
    public string Title { get; init; } = "Notes";
    public required AuthConfig Auth { get; init; }
}

/// <summary>How the app signs users in with its Keycloak realm.</summary>
public sealed class AuthConfig
{
    /// <summary>Issuer URL of the realm, e.g. http://localhost:8080/realms/notes.</summary>
    public required string Authority { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
}
