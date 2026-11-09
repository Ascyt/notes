using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using notes.Features.Auth;

namespace notes.Tests.Unit;

/// <summary>
/// Keeping the session in step with Keycloak (docs/administration/permissions.md, "Changes in Keycloak take effect within minutes"). Keycloak is replaced by
/// a fake token endpoint, and the app's clock by a fake one.
/// </summary>
[Trait("Category", "Timing")]
public sealed class SessionRefreshTests : IDisposable
{
    private const string Issuer = "https://auth.test/realms/notes";
    private const string ClientId = "notes-web";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "test" };
    private readonly ServiceProvider _services;
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<string> _requestBodies = [];

    /// <summary>What the fake Keycloak answers to a request for new tokens.</summary>
    private Func<HttpResponseMessage> _keycloak;

    public SessionRefreshTests()
    {
        _keycloak = () => TokenResponse(IdToken("alice-id", groups: ["team"]));

        OpenIdConnectConfiguration configuration = new() { Issuer = Issuer, TokenEndpoint = Issuer + "/protocol/openid-connect/token" };
        configuration.SigningKeys.Add(_key);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                options.ClientId = ClientId;
                options.ClientSecret = "secret";
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.Backchannel = new HttpClient(new FakeHandler(this));
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = NotesClaims.UserName;
                options.TokenValidationParameters.RoleClaimType = NotesClaims.Roles;
            });
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    // ---- The session and the fake Keycloak

    private sealed class FakeHandler(SessionRefreshTests test) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            test._requestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return test._keycloak();
        }
    }

    private string IdToken(string sub, string[]? groups = null, string[]? roles = null, string audience = ClientId, SecurityKey? key = null, string issuer = Issuer)
    {
        Dictionary<string, object> claims = new()
        {
            [NotesClaims.UserId] = sub,
            [NotesClaims.UserName] = "alice",
            [NotesClaims.Groups] = groups ?? [],
            [NotesClaims.Roles] = roles ?? []
        };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key ?? _key, SecurityAlgorithms.RsaSha256)
        });
    }

    private static HttpResponseMessage TokenResponse(string? idToken, string? refreshToken = "new-refresh", int expiresIn = 120, string? accessToken = "new-access")
    {
        Dictionary<string, object> body = new() { ["expires_in"] = expiresIn, ["token_type"] = "Bearer" };
        if (idToken is not null)
            body["id_token"] = idToken;
        if (refreshToken is not null)
            body["refresh_token"] = refreshToken;
        if (accessToken is not null)
            body["access_token"] = accessToken;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }

    /// <summary>A request with alice's session whose access token runs out at <paramref name="expiresAt"/>.</summary>
    private CookieValidatePrincipalContext Session(DateTimeOffset? expiresAt, string? refreshToken = "old-refresh", string method = "GET")
    {
        DefaultHttpContext http = new() { RequestServices = _services };
        http.Request.Method = method;

        AuthenticationProperties properties = new();
        List<AuthenticationToken> tokens = [new() { Name = "access_token", Value = "old-access" }, new() { Name = "id_token", Value = "old-id" }];
        if (expiresAt is { } expires)
            tokens.Add(new AuthenticationToken { Name = "expires_at", Value = expires.ToString("o", CultureInfo.InvariantCulture) });
        if (refreshToken is not null)
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
        properties.StoreTokens(tokens);

        ClaimsPrincipal principal = new TestUser("alice", "alice-id", ["old-group"], []).ToPrincipal();
        AuthenticationScheme scheme = new(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        return new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private static bool IsLoggedOut(CookieValidatePrincipalContext context) => context.Principal is null;

    private static string? Notice(CookieValidatePrincipalContext context) => SessionNotice.MessageFor(context.HttpContext);

    // ---- While the access token is good

    [Fact]
    public async Task A_session_whose_token_is_still_good_is_left_alone()
    {
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(1));

        await SessionRefresh.ValidateAsync(context);

        Assert.False(IsLoggedOut(context));
        Assert.Empty(_requests);
        Assert.False(context.ShouldRenew);
    }

    [Fact]
    public async Task A_token_about_to_run_out_is_renewed_already()
    {
        CookieValidatePrincipalContext context = Session(Now.AddSeconds(10));

        await SessionRefresh.ValidateAsync(context);

        Assert.Single(_requests);
        Assert.False(IsLoggedOut(context));
    }

    [Fact]
    public async Task Time_passing_is_what_makes_a_session_renew()
    {
        CookieValidatePrincipalContext before = Session(Now.AddMinutes(2));
        await SessionRefresh.ValidateAsync(before);
        Assert.Empty(_requests);

        _time.Advance(TimeSpan.FromMinutes(2));
        CookieValidatePrincipalContext after = Session(Now.AddMinutes(-0.5));
        await SessionRefresh.ValidateAsync(after);
        Assert.Single(_requests);
    }

    // ---- Renewing

    [Fact]
    public async Task Renewing_takes_the_groups_and_roles_the_user_has_now()
    {
        _keycloak = () => TokenResponse(IdToken("alice-id", groups: ["new-group"], roles: [NotesAccess.AdminRole]));
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.False(IsLoggedOut(context));
        Assert.True(context.ShouldRenew);
        Assert.Equal(["new-group"], context.Principal!.FindAll(NotesClaims.Groups).Select(c => c.Value));
        Assert.True(context.Principal.IsInRole(NotesAccess.AdminRole));
        Assert.Equal("alice", context.Principal.Identity!.Name);
        Assert.Equal("new-access", context.Properties.GetTokenValue("access_token"));
        Assert.Equal("new-refresh", context.Properties.GetTokenValue("refresh_token"));
        Assert.Equal(Now.AddSeconds(120), DateTimeOffset.Parse(context.Properties.GetTokenValue("expires_at")!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Renewing_asks_the_token_endpoint_with_the_refresh_token_and_the_client()
    {
        await SessionRefresh.ValidateAsync(Session(Now.AddMinutes(-1)));

        HttpRequestMessage request = Assert.Single(_requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Issuer + "/protocol/openid-connect/token", request.RequestUri!.ToString());
        Assert.Contains("grant_type=refresh_token", _requestBodies[0]);
        Assert.Contains("refresh_token=old-refresh", _requestBodies[0]);
        Assert.Contains("client_id=notes-web", _requestBodies[0]);
    }

    [Fact]
    public async Task Without_a_new_refresh_token_the_old_one_is_kept()
    {
        _keycloak = () => TokenResponse(IdToken("alice-id"), refreshToken: null);
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.Equal("old-refresh", context.Properties.GetTokenValue("refresh_token"));
    }

    // ---- What can't be trusted logs the user out

    [Fact]
    public async Task The_id_token_of_another_user_logs_out()
    {
        _keycloak = () => TokenResponse(IdToken("mallory-id", roles: [NotesAccess.AdminRole]));
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
        Assert.NotNull(Notice(context));
    }

    [Fact]
    public async Task An_id_token_signed_by_someone_else_logs_out()
    {
        _keycloak = () => TokenResponse(IdToken("alice-id", roles: [NotesAccess.AdminRole], key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test" }));
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
    }

    [Fact]
    public async Task An_id_token_for_another_client_logs_out()
    {
        _keycloak = () => TokenResponse(IdToken("alice-id", audience: "other-client"));
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
    }

    [Fact]
    public async Task An_id_token_of_another_issuer_logs_out()
    {
        _keycloak = () => TokenResponse(IdToken("alice-id", issuer: "https://evil.test/realms/notes"));
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
    }

    [Fact]
    public async Task An_unsigned_id_token_logs_out()
    {
        string unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = ClientId, Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { [NotesClaims.UserId] = "alice-id", [NotesClaims.Roles] = new[] { NotesAccess.AdminRole } }
        });
        _keycloak = () => TokenResponse(unsigned);
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task An_incomplete_answer_logs_out(bool withIdToken, bool withAccessToken)
    {
        _keycloak = () => TokenResponse(withIdToken ? IdToken("alice-id") : null, accessToken: withAccessToken ? "a" : null);
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_session_keycloak_ended_logs_out_with_a_notice(HttpStatusCode status)
    {
        _keycloak = () => new HttpResponseMessage(status) { Content = new StringContent("""{"error":"invalid_grant"}""") };
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1));

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
        Assert.Equal(SessionNotice.Ended, SessionNotice.Of(context.HttpContext));
    }

    [Fact]
    public async Task A_script_whose_session_ended_leaves_the_notice_for_the_next_page()
    {
        _keycloak = () => new HttpResponseMessage(HttpStatusCode.BadRequest);
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1), method: "POST");

        await SessionRefresh.ValidateAsync(context);

        Assert.Contains(context.HttpContext.Response.Headers.SetCookie, cookie => cookie!.StartsWith(".notes.notice=ended", StringComparison.Ordinal) && cookie.Contains("httponly"));
    }

    [Fact]
    public async Task A_session_without_the_time_its_token_runs_out_logs_out()
    {
        CookieValidatePrincipalContext context = Session(expiresAt: null);

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task A_session_without_a_refresh_token_logs_out_when_its_token_runs_out()
    {
        CookieValidatePrincipalContext context = Session(Now.AddMinutes(-1), refreshToken: null);

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
    }

    // ---- Keycloak can't be reached: kept for 10 minutes after the token ran out, then logged out

    public static TheoryData<string> Outages => ["503", "network", "garbage"];

    private void KeycloakIsDown(string how)
        => _keycloak = how switch
        {
            "503" => () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "network" => () => throw new HttpRequestException("connection refused"),
            _ => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>proxy error</html>") }
        };

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task A_short_outage_keeps_the_session(string how)
    {
        KeycloakIsDown(how);
        DateTimeOffset expiredAt = Now;
        _time.Advance(TimeSpan.FromMinutes(9));
        CookieValidatePrincipalContext context = Session(expiredAt);

        await SessionRefresh.ValidateAsync(context);

        Assert.False(IsLoggedOut(context));
        Assert.Null(Notice(context));
        // ...with what it said before: nothing is renewed
        Assert.Equal(["old-group"], context.Principal!.FindAll(NotesClaims.Groups).Select(c => c.Value));
    }

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task A_long_outage_logs_out_with_a_notice(string how)
    {
        KeycloakIsDown(how);
        DateTimeOffset expiredAt = Now;
        _time.Advance(TimeSpan.FromMinutes(11));
        CookieValidatePrincipalContext context = Session(expiredAt);

        await SessionRefresh.ValidateAsync(context);

        Assert.True(IsLoggedOut(context));
        Assert.Equal(SessionNotice.Unverified, SessionNotice.Of(context.HttpContext));
    }

    [Fact]
    public async Task When_keycloak_is_back_the_session_is_renewed()
    {
        KeycloakIsDown("503");
        DateTimeOffset expiredAt = Now;
        _time.Advance(TimeSpan.FromMinutes(5));
        await SessionRefresh.ValidateAsync(Session(expiredAt));

        _keycloak = () => TokenResponse(IdToken("alice-id", groups: ["team"]));
        _time.Advance(TimeSpan.FromMinutes(4));
        CookieValidatePrincipalContext context = Session(expiredAt);
        await SessionRefresh.ValidateAsync(context);

        Assert.False(IsLoggedOut(context));
        Assert.Equal(["team"], context.Principal!.FindAll(NotesClaims.Groups).Select(c => c.Value));
    }
}
