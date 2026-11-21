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

    // ---- One action per request

    [Theory]
    [InlineData("/docs?selection=clear&create=file")]
    [InlineData("/docs/a.md?rename=b.md&config")]
    [InlineData("/docs?create=directory&transfer=copy")]
    public async Task A_change_that_asks_for_two_actions_is_refused_instead_of_choosing_one(string url)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Admin, url));

        Assert.Equal(before, Tree.Snapshot());
    }

    [Theory]
    [InlineData("/docs/a.md?raw&download")]
    [InlineData("/docs/a.md?config&download")]
    [InlineData("/docs/a.md?selection&thumbnail")]
    [InlineData("/docs/a.md?before=2026-01-01T00:00:00Z&raw&download")]
    public async Task Reading_two_things_at_once_is_refused(string url)
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Admin, url));

    [Fact]
    public async Task Parameters_that_go_with_an_action_or_a_view_are_no_second_action()
    {
        // ?edit is a view of the file, not an action: the POST of the file is still its save
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md?edit", "saved"));
        Assert.Equal("saved", Tree.Read("docs/a.md"));
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
    public async Task Every_answer_forbids_framing_by_other_sites_sniffing_and_scripts_of_its_own(string url)
    {
        HttpResponseMessage response = await Client.GetAsync(Users.Alice, url);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        string policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'self'", policy);
        // Only the app's script files: nothing inline (which a note could carry), nothing from elsewhere
        Assert.Contains("script-src 'self';", policy);
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }
}
