using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace notes.Tests.Common;

/// <summary>Sends requests to the app as one of the <see cref="Users"/>, the way the pages' scripts do (with the antiforgery token).</summary>
public sealed class NotesClient(NotesAppFactory app)
{
    private readonly HttpClient _client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
    private readonly Dictionary<string, AntiforgeryTokens> _tokens = [];

    public Task<HttpResponseMessage> GetAsync(TestUser user, string url)
        => SendAsync(user, HttpMethod.Get, url);

    public Task<HttpResponseMessage> PostAsync(TestUser user, string url, string? text = null, bool antiforgery = true)
        => SendAsync(user, HttpMethod.Post, url, text is null ? null : new StringContent(text, Encoding.UTF8, "text/plain"), antiforgery);

    public Task<HttpResponseMessage> PostBytesAsync(TestUser user, string url, byte[] bytes)
        => SendAsync(user, HttpMethod.Post, url, new ByteArrayContent(bytes));

    public Task<HttpResponseMessage> PostJsonAsync<T>(TestUser user, string url, T body, bool antiforgery = true)
        => SendAsync(user, HttpMethod.Post, url, JsonContent.Create(body), antiforgery);

    public Task<HttpResponseMessage> DeleteAsync(TestUser user, string url, bool antiforgery = true)
        => SendAsync(user, HttpMethod.Delete, url, null, antiforgery);

    public async Task<HttpResponseMessage> SendAsync(TestUser user, HttpMethod method, string url, HttpContent? content = null, bool antiforgery = true)
    {
        using HttpRequestMessage request = new(method, url) { Content = content };
        if (user.ToHeader() is { } header)
            request.Headers.Add(TestUser.HeaderName, header);
        if (antiforgery && method != HttpMethod.Get && method != HttpMethod.Head)
        {
            AntiforgeryTokens tokens = TokensFor(user);
            request.Headers.Add("Cookie", tokens.Cookie);
            request.Headers.Add(tokens.HeaderName, tokens.RequestToken);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// A request with the path exactly as given, past any URL parsing of the client (which would resolve "..", decode escapes, and the like):
    /// what the app does with a path that a proxy or the server let through as it is.
    /// </summary>
    public async Task<RawResponse> RawAsync(TestUser user, string method, string path, string query = "", byte[]? body = null, bool antiforgery = true)
    {
        HttpContext context = await app.Server.SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = new PathString(path);
            context.Request.QueryString = query == "" ? QueryString.Empty : new QueryString(query.StartsWith('?') ? query : "?" + query);
            if (user.ToHeader() is { } header)
                context.Request.Headers[TestUser.HeaderName] = header;
            if (antiforgery && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
            {
                AntiforgeryTokens tokens = TokensFor(user);
                context.Request.Headers.Cookie = tokens.Cookie;
                context.Request.Headers[tokens.HeaderName] = tokens.RequestToken;
            }

            if (body is not null)
            {
                context.Request.Body = new MemoryStream(body);
                context.Request.ContentLength = body.Length;
                context.Request.ContentType = "application/octet-stream";
            }
        });

        string text = context.Response.Body is { CanRead: true } stream ? await new StreamReader(stream).ReadToEndAsync() : "";
        return new RawResponse((HttpStatusCode)context.Response.StatusCode, text, context.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));
    }

    private AntiforgeryTokens TokensFor(TestUser user)
    {
        string key = user.Sub ?? "";
        if (!_tokens.TryGetValue(key, out AntiforgeryTokens? tokens))
            _tokens[key] = tokens = app.TokensFor(user);
        return tokens;
    }
}

public sealed record RawResponse(HttpStatusCode StatusCode, string Body, IReadOnlyDictionary<string, string> Headers);
