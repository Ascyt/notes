using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace notes.Features.Auth;

public static class AuthExtensions
{
    /// <summary>Signs users in through Keycloak (OIDC code flow with PKCE) and keeps the session in a cookie.</summary>
    public static IServiceCollection AddNotesAuth(this IServiceCollection services, AuthConfig auth, bool requireHttpsMetadata)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = ".notes.auth";
                // Matches the idle timeout of a remembered Keycloak session (keycloak/notes-realm.json); Keycloak is what actually ends a session.
                // Sliding, so it only runs out for a user who stays away that long
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                // The user's groups and roles are copied into the cookie at login. This renews them from Keycloak when the access token runs out
                options.Events.OnValidatePrincipal = SessionRefresh.ValidateAsync;
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = auth.Authority;
                if (auth.MetadataAddress is not null)
                    options.MetadataAddress = auth.MetadataAddress;
                options.ClientId = auth.ClientId;
                options.ClientSecret = auth.ClientSecret;
                options.RequireHttpsMetadata = requireHttpsMetadata;

                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.SaveTokens = true; // the ID token is sent along when logging out of Keycloak

                // Keep Keycloak's claim names; the realm maps the user's realm roles to a flat "roles" claim and their groups to "groups"
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = NotesClaims.UserName;
                options.TokenValidationParameters.RoleClaimType = NotesClaims.Roles;

                // The response comes back as a top-level GET, so Lax cookies are enough (the default None needs HTTPS)
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                // Keycloak doesn't say whether "Remember me" was ticked, but it gives such a session a much longer lifetime (keycloak/notes-realm.json).
                // Only a remembered session keeps its cookie when the browser closes
                options.Events.OnTokenResponseReceived = context =>
                {
                    context.Properties!.IsPersistent = IsRemembered(context.TokenEndpointResponse.GetParameter("refresh_expires_in"));
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(NotesAccess.AdminPolicyName, policy => policy.RequireRole(NotesAccess.AdminRole));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, NotFoundAuthorizationResultHandler>();

        return services;
    }

    /// <summary>Longer than the maximum of a session without "Remember me" (keycloak/notes-realm.json), shorter than the idle timeout of one with it.</summary>
    private static readonly TimeSpan RememberedLifetime = TimeSpan.FromHours(8);

    /// <summary>Whether the session Keycloak granted at login is a remembered one, going by how long its refresh token lasts.</summary>
    public static bool IsRemembered(string? refreshExpiresIn)
        => int.TryParse(refreshExpiresIn, out int seconds) && TimeSpan.FromSeconds(seconds) > RememberedLifetime;
}
