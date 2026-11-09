using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace notes.Features.KillSwitch;

/// <summary>
/// Shuts the site down when an admin pulls the kill switch: the app answers every request with a "shut down" page from then on, also after a
/// restart, and the Keycloak realm is disabled so that nobody can log in or renew a session. Keycloak itself keeps running, so that the realm
/// can be enabled again from its admin console. See docs/kill-switch.md for how to bring the site back.
/// </summary>
/// <remarks>
/// The process keeps running: Docker would only start it again. The app can't stop the containers either, as that would need the Docker
/// socket, which is control over the whole host. The marker file is what keeps the site down across restarts.
/// </remarks>
public sealed class KillSwitchService
{
    private readonly Config _config;
    private readonly IOptionsMonitor<OpenIdConnectOptions> _oidcOptions;
    private readonly TimeProvider _time;
    private readonly ILogger<KillSwitchService> _logger;
    private volatile bool _isEngaged;

    public KillSwitchService(Config config, IWebHostEnvironment environment, IOptionsMonitor<OpenIdConnectOptions> oidcOptions, TimeProvider time, ILogger<KillSwitchService> logger)
    {
        _config = config;
        _oidcOptions = oidcOptions;
        _time = time;
        _logger = logger;
        // Next to the data protection keys, as that is the volume of the app's own state (the keys are only read from *.xml files)
        MarkerPath = config.KillSwitchMarker ?? Path.Combine(config.KeysDir ?? environment.ContentRootPath, "killed");
        _isEngaged = File.Exists(MarkerPath);
        if (_isEngaged)
            _logger.LogWarning("The kill switch was pulled ({marker} exists), so the site only answers that it is shut down", MarkerPath);
    }

    /// <summary>The file whose existence keeps the site shut down. Deleting it (and restarting the app) brings the site back.</summary>
    public string MarkerPath { get; }

    public bool IsEngaged => _isEngaged;

    /// <summary>Shuts the site down. Never fails: whatever can't be done is only logged (nothing of it reaches the browser), and the site is shut down regardless.</summary>
    public async Task EngageAsync(string adminName)
    {
        _isEngaged = true;
        _logger.LogWarning("The kill switch was pulled by {admin}", adminName);

        try
        {
            await File.WriteAllTextAsync(MarkerPath, $"Shut down by {adminName} at {_time.GetUtcNow():o}\n");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not write {marker}, so the site will be back up after a restart. Stop the container", MarkerPath);
        }

        try
        {
            await DisableRealmAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not disable the Keycloak realm. Disable it in the admin console");
        }
    }

    /// <summary>Through the admin API of Keycloak, as the service account of the app's own client (it has the realm's manage-realm role).</summary>
    private async Task DisableRealmAsync()
    {
        OpenIdConnectOptions options = _oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        OpenIdConnectConfiguration configuration = await options.ConfigurationManager!.GetConfigurationAsync(CancellationToken.None);

        using HttpRequestMessage tokenRequest = new(HttpMethod.Post, configuration.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _config.Auth.ClientId,
                ["client_secret"] = _config.Auth.ClientSecret
            })
        };
        using HttpResponseMessage tokenResponse = await options.Backchannel.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Keycloak answered {status} when asked for a token of the app's service account, so the realm is not disabled", (int)tokenResponse.StatusCode);
            return;
        }

        using JsonDocument tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        string? accessToken = tokens.RootElement.TryGetProperty("access_token", out JsonElement token) ? token.GetString() : null;
        if (accessToken is null)
        {
            _logger.LogError("Keycloak sent no access token for the app's service account, so the realm is not disabled");
            return;
        }

        using HttpRequestMessage disableRequest = new(HttpMethod.Put, AdminRealmUrl(_config.Auth.MetadataAddress ?? _config.Auth.Authority))
        {
            Content = JsonContent.Create(new { enabled = false })
        };
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage disableResponse = await options.Backchannel.SendAsync(disableRequest);
        if (!disableResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Keycloak answered {status} when asked to disable the realm", (int)disableResponse.StatusCode);
            return;
        }

        _logger.LogWarning("The Keycloak realm is disabled");
    }

    /// <summary>The admin API address of the realm, from an address within it (e.g. http://keycloak:8080/realms/notes/.well-known/openid-configuration).</summary>
    public static string AdminRealmUrl(string realmAddress)
    {
        const string realmsSegment = "/realms/";
        int realmsAt = realmAddress.IndexOf(realmsSegment, StringComparison.Ordinal);
        if (realmsAt < 0)
            throw new InvalidOperationException($"No realm in the address {realmAddress}");

        string server = realmAddress[..realmsAt];
        string realm = realmAddress[(realmsAt + realmsSegment.Length)..].Split('/')[0];
        return $"{server}/admin{realmsSegment}{realm}";
    }

    /// <summary>
    /// The page shown instead of anything else once the site is shut down. Self-contained, as nothing else of the app is served anymore, and
    /// vague on purpose: why the site is down is nobody's business but the admins'.
    /// </summary>
    public const string ShutdownPage = """
           <!DOCTYPE html>
           <html lang="en">
           <head>
               <meta charset="utf-8"/>
               <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
               <title>Shut down</title>
               <style>
                   body { font-family: system-ui, sans-serif; max-width: 36rem; margin: 4rem auto; padding: 0 1rem; color: #212529; background: #fff; }
                   @media (prefers-color-scheme: dark) { body { color: #dee2e6; background: #212529; } }
               </style>
           </head>
           <body>
               <h1>Shut down</h1>
               <p>This service has been shut down. Please check back later.</p>
           </body>
           </html>
           """;
}

public static class KillSwitchExtensions
{
    /// <summary>Answers every request with the shutdown page once the kill switch was pulled. Goes before everything else of the app.</summary>
    public static IApplicationBuilder UseKillSwitch(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            if (!context.RequestServices.GetRequiredService<KillSwitchService>().IsEngaged)
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(KillSwitchService.ShutdownPage);
        });
}
