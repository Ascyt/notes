using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Notes.Tests.Integration;

/// <summary>
/// The pages of the app at the root (<c>Config:AppPath</c> empty, security specification, "Pages outside the notes"): they come first, so the
/// names they take at the root are no part of the notes.
/// </summary>
public sealed class RootAppPathTests(RootAppPathNotesAppFactory app) : IntegrationTest(app), IClassFixture<RootAppPathNotesAppFactory>
{
    [Fact]
    public async Task The_pages_of_the_app_are_at_the_root()
    {
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, "/About"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/_/About"));
        // The links of the pages lead there
        Assert.Contains("href=\"/About\"", await BodyOf(await Client.GetAsync(Users.Alice, "/docs")));
    }

    [Fact]
    public async Task A_directory_with_the_name_of_a_page_is_hidden_from_everyone()
    {
        _ = Tree.File("About/x.md", "x");
        _ = Tree.File("docs/About/y.md", "y");

        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/About/x.md"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/about/x.md"));
        Assert.DoesNotContain("data-path=\"About\"", await BodyOf(await Client.GetAsync(Users.Admin, "/")));
        // Below the root the name is a name like any other
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Admin, "/docs/About/y.md"));
    }

    [Fact]
    public async Task No_new_entry_at_the_root_gets_the_name_of_a_page()
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, "/readme.md?rename=Login"));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, "/readme.md?rename=logout"));
        Assert.Equal(before, Tree.Snapshot());

        // Elsewhere it may
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/a.md?rename=Login"));
        Assert.True(Tree.IsFile("docs/Login"));
    }

    [Fact]
    public async Task Nothing_is_copied_or_uploaded_to_the_root_under_the_name_of_a_page()
    {
        _ = Tree.File("docs/Error", "not an error page");
        _ = await SelectAsync(Users.Alice, "docs/Error");

        await AssertStatus(HttpStatusCode.BadRequest, await TransferAsync(Users.Alice, "copy", "", "docs/Error"));
        Assert.False(Tree.Exists("Error"));

        HttpResponseMessage started = await Client.PostAsync(Users.Alice, "/?upload=start");
        await AssertStatus(HttpStatusCode.OK, started);
        HttpResponseMessage chunk = await Client.PostBytesAsync(Users.Alice, $"/?upload={await BodyOf(started)}&name=Shutdown&offset=0&last", [1, 2, 3]);
        await AssertStatus(HttpStatusCode.BadRequest, chunk);
        Assert.False(Tree.Exists("Shutdown"));
    }
}

/// <summary>The pages of the app at an address of their own (<c>Config:AppPath</c> <c>/app</c>), which takes its first name at the root.</summary>
public sealed class OtherAppPathTests(OtherAppPathNotesAppFactory app) : IntegrationTest(app), IClassFixture<OtherAppPathNotesAppFactory>
{
    [Fact]
    public async Task The_pages_of_the_app_are_at_its_address_and_its_name_is_no_part_of_the_notes()
    {
        _ = Tree.File("app/x.md", "x");

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, "/app/About"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/_/About"));
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Admin, "/app/x.md"));
        // Pages that aren't there answer with the error page at the app's address
        Assert.Contains("href=\"/app/About\"", await BodyOf(await Client.GetAsync(Users.Alice, "/docs")));
    }
}

/// <summary>What <c>Config:AppPath</c> may be.</summary>
public sealed class AppPathSettingTests
{
    [Theory]
    [InlineData("app")]
    [InlineData("/a b")]
    [InlineData("/app?x")]
    [InlineData("//")]
    public void An_address_that_is_none_does_not_let_the_app_start(string appPath)
    {
        using NotesAppFactory app = new();
        using var withSetting = app.WithWebHostBuilder(builder => builder.UseSetting("Config:AppPath", appPath));

        Exception refused = Assert.ThrowsAny<Exception>(() => withSetting.CreateClient());

        Assert.Contains("Config:AppPath", refused.ToString());
    }
}
