using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace notes.Features.Auth;

/// <summary>
/// Keeps the session in step with Keycloak. The groups and roles that the permissions are made of are copied from the tokens into the session
/// cookie when the user logs in, so on their own they would stay what they were at login for as long as the session lasts. Instead the access
/// token is short-lived (see keycloak/notes-realm.json), and when it has run out the session asks Keycloak for a new one with the refresh token,
/// which carries the user's groups and roles as they are by then. So a change made in Keycloak takes effect within the lifetime of an access token.
/// </summary>
/// <remarks>
/// If Keycloak says the session is over (the user logged out elsewhere, was disabled or deleted, the session expired), or can't be reached
/// for long enough that nothing can be said about it, the user is logged out, and is told why on the next page (<see cref="SessionNotice"/>).
/// What the session says is never kept for longer than that.
/// </remarks>
public static class SessionRefresh
{
    /// <summary>A token that is about to run out is renewed already, so that a request never gets to use one that has.</summary>
    private static readonly TimeSpan RenewMargin = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long after its access token ran out a session is kept if Keycloak can't be asked (it is down or not reachable). A short outage doesn't log
    /// everyone out, but what the session says about a user is not trusted for long without Keycloak's word.
    /// </summary>
    private static readonly TimeSpan OutageGrace = TimeSpan.FromMinutes(10);

    private enum Outcome { Renewed, Over, Unreachable }

    /// <summary>Where the time a request spent on its session (when it had to ask Keycloak) is kept, for the log of slow requests.</summary>
    public const string ElapsedItemKey = "notes.session-check-ms";

    /// <summary>For <see cref="CookieAuthenticationEvents.OnValidatePrincipal"/>: runs for every request that comes with a session.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        await ValidateCoreAsync(context);
        context.HttpContext.Items[ElapsedItemKey] = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static async Task ValidateCoreAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        // A session without the time its token runs out was not made by this version of the app: it can't be kept in step
        if (!DateTimeOffset.TryParse(context.Properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expiresAt))
        {
            await EndAsync(context, SessionNotice.Ended);
            return;
        }

        DateTimeOffset now = ClockOf(context.HttpContext).GetUtcNow();
        if (expiresAt - RenewMargin > now)
            return;

        ILogger logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SessionRefresh));
        switch (await RenewAsync(context, logger))
        {
            case Outcome.Renewed:
                break;
            case Outcome.Over:
                await EndAsync(context, SessionNotice.Ended);
                break;
            case Outcome.Unreachable when now - expiresAt > OutageGrace:
                await EndAsync(context, SessionNotice.Unverified);
                break;
            case Outcome.Unreachable:
                break; // try again with the next request
        }
    }

    /// <summary>The app's clock (replaced by the tests), or the system's if there is none.</summary>
    private static TimeProvider ClockOf(HttpContext context)
        => context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

    private static async Task EndAsync(CookieValidatePrincipalContext context, string notice)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        SessionNotice.Set(context.HttpContext, notice);
    }

    private static async Task<Outcome> RenewAsync(CookieValidatePrincipalContext context, ILogger logger)
    {
        OpenIdConnectOptions options = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        string? refreshToken = context.Properties.GetTokenValue("refresh_token");
        if (refreshToken is null)
            return Outcome.Over;

        CancellationToken cancellation = context.HttpContext.RequestAborted;
        try
        {
            // Through the same address and client as the login itself used for the code
            OpenIdConnectConfiguration configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellation);
            using HttpRequestMessage request = new(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = options.ClientId!,
                    ["client_secret"] = options.ClientSecret!
                })
            };
            using HttpResponseMessage response = await options.Backchannel.SendAsync(request, cancellation);

            // Keycloak answers 400 (invalid_grant) for a session that is over, and 401 for a client it doesn't know. Anything else is its trouble.
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
            {
                logger.LogInformation("Keycloak refused to renew a session ({status}), so the user is logged out", (int)response.StatusCode);
                return Outcome.Over;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Keycloak answered {status} when renewing a session", (int)response.StatusCode);
                return Outcome.Unreachable;
            }

            using JsonDocument tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            return await ApplyAsync(context, options, configuration, tokens.RootElement, refreshToken, logger)
                ? Outcome.Renewed
                : Outcome.Over;
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException or IOException or JsonException) && !cancellation.IsCancellationRequested)
        {
            logger.LogWarning(e, "Could not reach Keycloak to renew a session");
            return Outcome.Unreachable;
        }
    }

    /// <summary>Takes what Keycloak answered into the session: the new tokens, and the user with the groups and roles they have now. False if the answer can't be trusted.</summary>
    private static async Task<bool> ApplyAsync(CookieValidatePrincipalContext context, OpenIdConnectOptions options, OpenIdConnectConfiguration configuration,
        JsonElement tokens, string oldRefreshToken, ILogger logger)
    {
        string? accessToken = tokens.TryGetProperty("access_token", out JsonElement a) ? a.GetString() : null;
        string? idToken = tokens.TryGetProperty("id_token", out JsonElement i) ? i.GetString() : null;
        string refreshToken = tokens.TryGetProperty("refresh_token", out JsonElement r) && r.GetString() is { } renewed ? renewed : oldRefreshToken;
        if (accessToken is null || idToken is null || !tokens.TryGetProperty("expires_in", out JsonElement expiresIn) || !expiresIn.TryGetInt32(out int seconds))
        {
            logger.LogWarning("Keycloak's answer when renewing a session is incomplete, so the user is logged out");
            return false;
        }

        // The user is read from the ID token, checked like the one of the login (it comes straight from Keycloak over a secure connection, but
        // what the permissions rest on is not taken on trust)
        TokenValidationParameters parameters = options.TokenValidationParameters.Clone();
        parameters.ValidIssuer = configuration.Issuer;
        parameters.IssuerSigningKeys = configuration.SigningKeys;
        parameters.ValidAudience = options.ClientId;
        TokenValidationResult validated = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters);
        if (!validated.IsValid)
        {
            logger.LogWarning(validated.Exception, "The ID token Keycloak sent when renewing a session is not valid, so the user is logged out");
            return false;
        }

        ClaimsIdentity identity = validated.ClaimsIdentity;
        // Still the user the session is of
        if (identity.FindFirst(NotesClaims.UserId)?.Value != context.Principal!.FindFirst(NotesClaims.UserId)?.Value)
        {
            logger.LogWarning("Keycloak sent the ID token of another user when renewing a session, so the user is logged out");
            return false;
        }

        // The same claims end up in the session as at login, no more (the ones the options drop are only meant for the login itself)
        using (JsonDocument none = JsonDocument.Parse("{}"))
        {
            foreach (ClaimAction action in options.ClaimActions)
                action.Run(none.RootElement, identity, options.ClaimsIssuer ?? "");
        }

        Dictionary<string, string> stored = context.Properties.GetTokens().ToDictionary(token => token.Name, token => token.Value ?? "");
        stored["access_token"] = accessToken;
        stored["id_token"] = idToken;
        stored["refresh_token"] = refreshToken;
        stored["expires_at"] = ClockOf(context.HttpContext).GetUtcNow().AddSeconds(seconds).ToString("o", CultureInfo.InvariantCulture);
        context.Properties.StoreTokens(stored.Select(token => new AuthenticationToken { Name = token.Key, Value = token.Value }));

        context.ReplacePrincipal(new ClaimsPrincipal(identity));
        context.ShouldRenew = true;
        return true;
    }
}
