using System.Net;

namespace notes.Tests.Integration;

/// <summary>Forged requests (CSRF), what is outside of the notes (admin dashboard, login), and methods that are not used.</summary>
public sealed class RequestProtectionTests(NotesAppFactory app) : IntegrationTest(app)
{
    public static TheoryData<string, string, string?> ChangingRequests => new()
    {
        { "POST", "/docs/a.md", "forged" },
        { "POST", "/docs/a.md?rename=forged.md", null },
        { "DELETE", "/docs/a.md", null },
        { "POST", "/docs?create=file", null },
        { "POST", "/docs?upload=start", null },
        { "POST", "/?selection=add", """{ "paths": ["docs/a.md"] }""" },
        { "POST", "/docs?transfer=delete", """{ "version": 0 }""" },
        { "POST", "/docs?config", """{ "perms": { "inherit": false, "rwAllow": ["u:mallory"] }, "files": [] }""" },
        { "POST", "/_/Logout", null }
    };

    private static HttpContent? Body(string? text)
        => text is null ? null : new StringContent(text, System.Text.Encoding.UTF8, text.StartsWith('{') ? "application/json" : "text/plain");

    [Theory]
    [MemberData(nameof(ChangingRequests))]
    public async Task Requests_that_change_something_need_the_antiforgery_token(string method, string url, string? body)
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await Client.SendAsync(Users.Admin, new HttpMethod(method), url, Body(body), antiforgery: false);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Equal(before, Tree.Snapshot());
        Assert.Empty((await GetSelectionAsync(Users.Admin)).Paths);
    }

    [Theory]
    [MemberData(nameof(ChangingRequests))]
    public async Task The_token_of_another_user_does_not_do(string method, string url, string? body)
    {
        string before = Tree.Snapshot();
        AntiforgeryTokens mallorys = App.TokensFor(Users.Carol);

        using HttpRequestMessage request = new(new HttpMethod(method), url) { Content = Body(body) };
        request.Headers.Add(TestUser.HeaderName, Users.Admin.ToHeader());
        request.Headers.Add("Cookie", mallorys.Cookie);
        request.Headers.Add(mallorys.HeaderName, mallorys.RequestToken);
        HttpResponseMessage response = await App.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }).SendAsync(request, Cancellation);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task The_token_in_a_form_field_works_too_as_the_create_buttons_send_it()
    {
        AntiforgeryTokens tokens = App.TokensFor(Users.Alice);
        using HttpRequestMessage request = new(HttpMethod.Post, "/docs?create=file")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", tokens.RequestToken)])
        };
        request.Headers.Add(TestUser.HeaderName, Users.Alice.ToHeader());
        request.Headers.Add("Cookie", tokens.Cookie);

        HttpResponseMessage response = await App.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }).SendAsync(request, Cancellation);

        await AssertStatus(HttpStatusCode.Redirect, response);
    }

    [Fact]
    public async Task The_hangfire_dashboard_is_missing_for_everyone_but_admins()
    {
        foreach (TestUser user in new[] { Users.Alice, Users.Bob, Users.Anonymous, Users.Nobody })
        {
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(user, "/_/Hangfire"));
            await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(user, "/_/Hangfire/recurring"));
            await AssertStatus(HttpStatusCode.NotFound, await Client.SendAsync(user, HttpMethod.Post, "/_/Hangfire/recurring/trigger", new FormUrlEncodedContent([new("jobs[]", "clear-stale-uploads")])));
        }
    }

    [Theory]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("\\\\evil.example", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("/docs?download", "/docs?download")]
    [InlineData("/docs", "/docs")]
    public async Task Logging_in_only_ever_leads_back_into_the_app(string returnUrl, string expected)
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, "/_/Login?returnUrl=" + Uri.EscapeDataString(returnUrl));

        await AssertStatus(HttpStatusCode.Redirect, response);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task The_error_page_is_no_page_of_its_own()
        => await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/_/Error"));

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Methods_that_are_not_used_change_nothing(string method)
    {
        string before = Tree.Snapshot();

        HttpResponseMessage response = await Client.SendAsync(Users.Admin, new HttpMethod(method), "/docs/a.md", new StringContent("x"));

        // The page has no handler for them, so it is made without one: nothing of the entry is in it
        Assert.DoesNotContain("const canWrite", await BodyOf(response));
        Assert.Equal(before, Tree.Snapshot());
    }

    [Fact]
    public async Task A_forged_session_notice_shows_nothing_but_the_known_texts()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/docs");
        request.Headers.Add("Cookie", ".notes.notice=<script>alert(1)</script>");
        HttpResponseMessage response = await App.CreateClient().SendAsync(request, Cancellation);

        Assert.DoesNotContain("<script>alert(1)", await BodyOf(response));
    }
}
