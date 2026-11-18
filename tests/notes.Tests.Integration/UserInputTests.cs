using System.Net;

namespace notes.Tests.Integration;

/// <summary>How the app takes the address and the query apart, and what every answer tells the browser (docs/development/user-input.md).</summary>
public sealed class UserInputTests(NotesAppFactory app) : IntegrationTest(app)
{
    // ---- Parameters given more than once

    [Fact]
    public async Task A_parameter_given_twice_is_refused_instead_of_joined()
    {
        string before = Tree.Snapshot();

        // Read as one value, this would be "x.md,y.md": a valid name
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, "/docs/a.md?rename=x.md&rename=y.md"));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.DeleteAsync(Users.Alice, "/docs/a.md?x&x"));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Alice, "/docs/a.md?raw&raw"));
        Assert.Equal(before, Tree.Snapshot());
    }

    // ---- Links to entries whose names mean something in an address

    [Theory]
    [InlineData("c# notes.md", "c%23%20notes.md")]
    [InlineData("100%.md", "100%25.md")]
    [InlineData("%2e%2e", "%252e%252e")]
    public async Task Links_to_entries_keep_their_whole_name(string name, string escaped)
    {
        _ = Tree.File("docs/" + name, "contents");

        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));

        Assert.Contains($"href=\"/docs/{escaped}\"", listing);
        HttpResponseMessage opened = await Client.GetAsync(Users.Alice, $"/docs/{escaped}?download");
        await AssertStatus(HttpStatusCode.OK, opened);
        Assert.Equal("contents", await BodyOf(opened));
    }

    [Fact]
    public async Task A_rename_answers_with_the_address_of_the_new_name()
    {
        HttpResponseMessage response = await Client.PostAsync(Users.Alice, "/docs/a.md?rename=" + Uri.EscapeDataString("c# 100%.md"));

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("/docs/c%23%20100%25.md", await BodyOf(response));
    }

    // ---- Parameters of the history that can't be a state

    [Theory]
    [InlineData("commit=HEAD")]
    [InlineData("commit=main~1")]
    [InlineData("before=yesterday")]
    public async Task The_list_of_changes_refuses_what_is_no_state(string state)
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Alice, "/docs/a.md?commits&" + state));

    // ---- Headers of every answer

    [Theory]
    [InlineData("/docs/a.md")]
    [InlineData("/does/not/exist")]
    [InlineData("/_/About")]
    public async Task Every_answer_forbids_framing_by_other_sites_and_sniffing(string url)
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, url);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }
}
