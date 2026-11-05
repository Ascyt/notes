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
                // Roles are copied into the cookie at login, so this also bounds how long a revoked role keeps working
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
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

                // Keep Keycloak's claim names; the realm maps the user's realm roles to a flat "roles" claim
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = "roles";

                // The response comes back as a top-level GET, so Lax cookies are enough (the default None needs HTTPS)
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(NotesAccess.PolicyName, policy => policy.AddRequirements(new NotesAccessRequirement()));
        services.AddSingleton<IAuthorizationHandler, NotesAccessHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, NotFoundAuthorizationResultHandler>();

        return services;
    }
}
