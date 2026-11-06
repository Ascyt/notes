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
                // Matches the session of Keycloak (keycloak/notes-realm.json), which is what actually ends it
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
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
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(NotesAccess.AdminPolicyName, policy => policy.RequireRole(NotesAccess.AdminRole));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, NotFoundAuthorizationResultHandler>();

        return services;
    }
}
