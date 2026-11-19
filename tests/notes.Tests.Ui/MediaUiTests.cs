using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using notes.Features.Components.Selection;

namespace notes.Tests.Ui;

/// <summary>The grid of media of a listing, the way from one media file to the next, and the parts of a page that stay at the top while it scrolls.</summary>
public sealed class MediaUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private static byte[] Png()
    {
        using MagickImage image = new(MagickColors.Orange, 64, 48) { Format = MagickFormat.Png };
        return image.ToByteArray();
    }

    private IReadOnlyList<string> ServerSelection
        => App.Services.GetRequiredService<ISelectionService>().Get(Users.Alice.Sub!).Paths;

    // A key pressed while the next page is still coming would go nowhere: its buttons get their tooltips once its scripts have run
    private async Task WaitForMediaPageAsync(string name)
    {
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/docs/pics/" + System.Text.RegularExpressions.Regex.Escape(name) + "$"));
        await Expect(Page.Locator("#next-media-button")).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex(@"\S"));
    }

    // Three images, in the order of the grid: c, b, a
    private void AddPictures()
    {
        foreach (string name in new[] { "a", "b", "c" })
            _ = Tree.File($"docs/pics/{name}.png", Png());
    }

    [Fact]
    public async Task Alt_and_a_number_opens_a_media_file_and_the_arrow_keys_go_through_them()
    {
        AddPictures();
        await OpenAsync("/docs/pics");
        await Expect(Page.Locator(".media-tile img").First).ToHaveJSPropertyAsync("complete", true);

        await Page.Keyboard.PressAsync("Alt+1");
        await WaitForMediaPageAsync("c.png");

        // The first has nothing before it
        await ExpectDisabledWithReason(Page.Locator("#previous-media-button"));
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.EndsWith("/docs/pics/c.png", Page.Url);

        await Page.Keyboard.PressAsync("ArrowRight");
        await WaitForMediaPageAsync("b.png");
        await Page.Keyboard.PressAsync("ArrowRight");
        await WaitForMediaPageAsync("a.png");
        await ExpectDisabledWithReason(Page.Locator("#next-media-button"));
        await Page.Keyboard.PressAsync("ArrowLeft");
        await WaitForMediaPageAsync("b.png");
    }

    [Fact]
    public async Task The_arrow_keys_are_left_to_a_video_that_has_the_focus()
    {
        _ = Tree.File("docs/pics/a.png", Png());
        _ = Tree.File("docs/pics/b.mp4", [0]);
        await OpenAsync("/docs/pics/b.mp4");

        await Page.Locator("video").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.EndsWith("/docs/pics/b.mp4", Page.Url);
    }

    [Fact]
    public async Task Media_are_selected_with_their_boxes_alt_shift_and_a_number_and_alt_a_for_all()
    {
        AddPictures();
        await OpenAsync("/docs/pics");

        await Page.Keyboard.PressAsync("Alt+Shift+1");
        await Expect(Page.Locator(".media-tile[data-path='docs/pics/c.png'] .entry-select")).ToBeCheckedAsync();
        await Eventually(() => ServerSelection.SequenceEqual(["docs/pics/c.png"]), "the server has the selection");

        // A dash for some, then all of them
        await Page.Keyboard.PressAsync("Alt+a");
        await Eventually(() => ServerSelection.Count == 0, "selecting all of a group with some selected unselects it");
        await Page.Keyboard.PressAsync("Alt+a");
        await Eventually(() => ServerSelection.Count == 3, "all media are selected");
        await Page.Locator("#selection-clear").ClickAsync();
        await Eventually(() => ServerSelection.Count == 0, "the selection is cleared");
    }

    [Fact]
    public async Task An_image_whose_thumbnail_cant_be_made_shows_an_icon()
    {
        _ = Tree.File("docs/pics/broken.png", "not an image");
        await OpenAsync("/docs/pics");

        await Expect(Page.Locator(".media-tile img")).ToBeHiddenAsync();
        await Expect(Page.Locator(".media-tile .media-placeholder")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_breadcrumb_stays_right_below_the_navbar_while_the_page_scrolls_and_the_toolbar_does_not()
    {
        for (int i = 0; i < 80; i++)
            _ = Tree.File($"docs/long/file{i:00}.md", "x");
        await OpenAsync("/docs/long");

        // Already where it stays, so it doesn't move at the start of scrolling
        await AssertBreadcrumbRightBelowNavbarAsync();

        _ = await Page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        _ = await Page.WaitForFunctionAsync("() => window.scrollY > 500");
        await AssertBreadcrumbRightBelowNavbarAsync();
        LocatorBoundingBoxResult toolbar = (await Page.Locator("[role=toolbar]").First.BoundingBoxAsync())!;
        Assert.True(toolbar.Y + toolbar.Height < 0, "The toolbar scrolls away with the page.");
    }

    private async Task AssertBreadcrumbRightBelowNavbarAsync()
    {
        LocatorBoundingBoxResult navbar = (await Page.Locator("#site-header").BoundingBoxAsync())!;
        LocatorBoundingBoxResult breadcrumb = (await Page.Locator(".sticky-breadcrumb").BoundingBoxAsync())!;
        Assert.InRange(navbar.Y, -0.5, 0.5);
        Assert.InRange(breadcrumb.Y, navbar.Y + navbar.Height - 0.5, navbar.Y + navbar.Height + 0.5);
    }
}
