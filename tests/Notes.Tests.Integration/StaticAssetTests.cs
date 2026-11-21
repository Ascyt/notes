using System.Net;

namespace Notes.Tests.Integration;

public sealed class StaticAssetTests(NotesAppFactory app) : IntegrationTest(app)
{
    // The library's own files are served under its name (_content/Notes), the site's (the favicon) at the root
    [Theory]
    [InlineData("/_content/Notes/Pages/Shared/AppLayout/site.js")]
    [InlineData("/_content/Notes/Features/Editor/editor.js")]
    [InlineData("/_content/Notes/Features/Shortcuts/shortcuts.js")]
    [InlineData("/_content/Notes/Pages/Shared/AppLayout/site.css")]
    public async Task The_scripts_and_styles_of_the_pages_are_served(string path)
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md"));
        string? versioned = System.Text.RegularExpressions.Regex.Match(page, $"\"({System.Text.RegularExpressions.Regex.Escape(path)}[^\"]*)\"").Groups[1].Value;

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, path));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, versioned));
    }
}
