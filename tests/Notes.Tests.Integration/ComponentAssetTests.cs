using System.Net;
using System.Text.RegularExpressions;
using Notes.Features.Components.Assets;
using Notes.Features.Components.Breadcrumb;
using Notes.Features.Components.Toolbar;
using Notes.Pages.Browse;
using Notes.Pages.Browse.EntryTable;
using Notes.Pages.Browse.MediaGrid;

namespace Notes.Tests.Integration;

/// <summary>The styles and scripts of the components, kept next to their views (docs/library.md, "Components").</summary>
public sealed class ComponentAssetTests(NotesAppFactory app) : IntegrationTest(app)
{
    [Theory]
    [InlineData(_Toolbar.Styles)]
    [InlineData(_Breadcrumb.Styles)]
    [InlineData(_Breadcrumb.Script)]
    [InlineData(DirectoryView.Styles)]
    [InlineData(_EntryGroup.Styles)]
    [InlineData(_MediaGroup.Styles)]
    public async Task They_are_on_the_page_by_an_address_with_their_fingerprint_and_served(string path)
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md"));

        string? linked = ComponentAssetPages.AddressOn(page, path);
        Assert.NotNull(linked);
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, linked));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Anonymous, ComponentAssets.LibraryAddress + path));
    }
}

/// <summary>A site that replaces some styles, scripts and texts of the components (<see cref="SiteComponentsNotesAppFactory"/>).</summary>
public sealed class SiteComponentTests(SiteComponentsNotesAppFactory app) : IntegrationTest(app), IClassFixture<SiteComponentsNotesAppFactory>
{
    [Fact]
    public async Task What_the_site_replaces_takes_the_place_of_the_components_own()
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/a.md"));

        Assert.Null(ComponentAssetPages.AddressOn(page, _Toolbar.Styles));
        Assert.Contains($"href=\"{SiteComponentsNotesAppFactory.ToolbarStyles}\"", page);
        Assert.Null(ComponentAssetPages.AddressOn(page, _Breadcrumb.Script));
        // What it doesn't replace stays
        Assert.NotNull(ComponentAssetPages.AddressOn(page, _Breadcrumb.Styles));
    }

    [Fact]
    public async Task Its_own_stylesheet_comes_after_those_of_the_components_so_that_it_overrides_them()
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));

        int added = page.IndexOf($"href=\"{SiteComponentsNotesAppFactory.AddedStyles}\"", StringComparison.Ordinal);
        int ofAComponent = page.IndexOf(ComponentAssetPages.AddressOn(page, _MediaGroup.Styles)!, StringComparison.Ordinal);
        Assert.True(added > ofAComponent, "after the styles of the components");
    }

    [Fact]
    public async Task Its_texts_replace_those_it_overrides_and_the_others_stay()
    {
        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));

        Assert.Contains($"data-label=\"{SiteComponentsNotesAppFactory.SiteToolbarTexts.SiteUpload}\"", page);
        Assert.Contains(SiteComponentsNotesAppFactory.SiteListingTexts.SiteDirectories, page);
        Assert.Contains($"data-label=\"{new ToolbarTexts().Download(isFile: false)}\"", page);
    }
}

internal static class ComponentAssetPages
{
    /// <summary>The address a page loads a style or script of a component from (with its fingerprint in its name), or null if it doesn't.</summary>
    public static string? AddressOn(string page, string path)
    {
        string address = ComponentAssets.LibraryAddress + path;
        int extension = address.LastIndexOf('.');
        Match found = Regex.Match(page,
            $"(?:href|src)=\"({Regex.Escape(address[..extension])}\\.[a-z0-9]+{Regex.Escape(address[extension..])})\"");
        return found.Success ? found.Groups[1].Value : null;
    }
}
