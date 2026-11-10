using System.Net;

namespace notes.Tests.Integration;

public sealed class StaticAssetTests(NotesAppFactory app) : IntegrationTest(app)
{
    [Theory]
    [InlineData("/js/site.js")]
    [InlineData("/js/editor/editor.js")]
    [InlineData("/shortcuts/shortcuts.js")]
    [InlineData("/css/site.css")]
    public async Task The_scripts_and_styles_of_the_pages_are_served(string path)
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md"));
        string? versioned = System.Text.RegularExpressions.Regex.Match(page, $"\"({System.Text.RegularExpressions.Regex.Escape(path)}[^\"]*)\"").Groups[1].Value;

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, path));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, versioned));
    }
}
