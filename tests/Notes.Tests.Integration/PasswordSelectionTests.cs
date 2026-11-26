using System.Net;
using System.Text.Json.Nodes;

namespace Notes.Tests.Integration;

/// <summary>
/// Selecting what a password locks: it can be selected, the selection says it is locked for this browser (and which passwords unlock it), and
/// nothing can be done with it until it is unlocked (a copy, move or delete asks for its password first).
/// </summary>
public sealed class PasswordSelectionTests(NotesAppFactory app) : PasswordTest(app)
{
    private async Task<JsonNode> SelectAsync(params string[] paths)
    {
        await AssertStatus(HttpStatusCode.OK, await Client.PostJsonAsync(Users.Alice, "/?selection=clear", new { paths = Array.Empty<string>() }));
        HttpResponseMessage added = await Client.PostJsonAsync(Users.Alice, "/?selection=add", new { paths });
        await AssertStatus(HttpStatusCode.OK, added);
        return JsonNode.Parse(await BodyOf(added))!;
    }

    private static string[] ListOf(JsonNode selection, string key)
        => [.. selection["status"]![key]!.AsArray().Select(item => (string)item!)];

    [Fact]
    public async Task What_a_password_locks_can_be_selected_and_the_selection_says_what_unlocks_it()
    {
        JsonNode selection = await SelectAsync("safe", "papers/pw.md", "papers/plain.md");

        Assert.Equal(["papers/plain.md", "papers/pw.md", "safe"], selection["paths"]!.AsArray().Select(path => (string)path!));
        Assert.Equal(["papers/pw.md", "safe"], ListOf(selection, "locked").Order());
        Assert.Equal(["/safe", "/papers/pw.md"], ListOf(selection, "unlock"));

        // Its row can be ticked like the others
        string root = await BodyOf(await Client.GetAsync(Users.Alice, "/"));
        Assert.Matches(@"<tr class=""entry-row[^""]*"" data-group=""directories"" data-path=""safe""\s+data-password-locked-entry=""safe""", root);
    }

    [Fact]
    public async Task What_the_user_may_not_read_anyway_still_cant_be_selected()
    {
        JsonNode selection = await SelectAsync("vault/pw");

        Assert.Empty(selection["paths"]!.AsArray());
    }

    [Theory]
    [InlineData("move")]
    [InlineData("copy")]
    [InlineData("delete")]
    public async Task Nothing_is_done_with_a_locked_selection_before_its_password_is_given(string kind)
    {
        JsonNode selection = await SelectAsync("safe");

        HttpResponseMessage refused = await Client.PostJsonAsync(Users.Alice, "/docs?transfer=" + kind, new { version = (long)selection["version"]! });

        await AssertPasswordRequired("/safe", refused);
        Assert.Equal("# Inside\nsecret text", Tree.Read("safe/inside.md"));
        Assert.False(Tree.Exists("docs/safe"));
        // Still selected, as it was: sent again with the password, it goes through
        JsonNode after = JsonNode.Parse(await BodyOf(await Client.GetAsync(Users.Alice, "/?selection")))!;
        Assert.Equal((long)selection["version"]!, (long)after["version"]!);
    }

    [Fact]
    public async Task Once_unlocked_it_is_no_longer_locked_but_unlocked()
    {
        _ = await SelectAsync("safe");
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/safe", SafePassword);

        JsonNode selection = JsonNode.Parse(await BodyOf(await GetWithCookieAsync(Users.Alice, "/?selection", cookie!)))!;

        Assert.Empty(ListOf(selection, "locked"));
        Assert.Equal(["/safe"], ListOf(selection, "unlocked"));
    }

    [Fact]
    public async Task What_no_password_locks_is_moved_as_before()
    {
        JsonNode selection = await SelectAsync("papers/plain.md");

        HttpResponseMessage moved = await Client.PostJsonAsync(Users.Alice, "/docs?transfer=move", new { version = (long)selection["version"]! });

        await AssertStatus(HttpStatusCode.OK, moved);
        Assert.Equal("plain", Tree.Read("docs/plain.md"));
    }

    [Fact]
    public async Task A_download_of_a_locked_selection_asks_for_its_password_first()
    {
        JsonNode selection = await SelectAsync("safe", "papers/plain.md");

        HttpResponseMessage refused = await Client.PostJsonAsync(Users.Alice, "/?archive=selection",
            new { format = "zip", compression = "store", version = (long)selection["version"]! });

        await AssertPasswordRequired("/safe", refused);
    }
}
