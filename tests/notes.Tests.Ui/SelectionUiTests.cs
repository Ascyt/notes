using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using notes.Features.Components.Selection;

namespace notes.Tests.Ui;

/// <summary>The selection card and the checkboxes of the listing (_SelectionBar.cshtml).</summary>
public sealed class SelectionUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator Box(string path) => Page.Locator($"tr.entry-row[data-path='{path}'] .entry-select");
    private ILocator Bar => Page.Locator("#selection-bar");
    private ILocator CopyButton => Page.Locator("#selection-copy");
    private ILocator MoveButton => Page.Locator("#selection-move");
    private ILocator DeleteButton => Page.Locator("#selection-delete");

    private IReadOnlyList<string> ServerSelection
        => App.Services.GetRequiredService<ISelectionService>().Get(Users.Alice.Sub!).Paths;

    private async Task SelectAsync(string directory, params string[] paths)
    {
        await OpenAsync("/" + directory);
        foreach (string path in paths)
            await Box(path).CheckAsync();
        await Eventually(() => paths.All(ServerSelection.Contains), "the server has the selection");
        // ...and the page has its answer: the card shows the count the server gave (and its buttons have their shortcuts)
        await Expect(Page.Locator("#selection-count")).ToHaveTextAsync(paths.Length + (paths.Length == 1 ? " element selected" : " elements selected"));
        await Expect(Page.Locator("#selection-clear")).ToHaveAttributeAsync("key", "Escape");
    }

    [Fact]
    public async Task Ticking_a_box_selects_and_the_card_counts_it()
    {
        await OpenAsync("/docs");
        await Expect(Bar).ToBeHiddenAsync();

        await Box("docs/a.md").CheckAsync();

        await Expect(Bar).ToBeVisibleAsync();
        await Expect(Page.Locator("#selection-count")).ToHaveTextAsync("1 element selected");
        await Expect(Page.Locator("tr.entry-row[data-path='docs/a.md']")).ToHaveClassAsync(new Regex("table-active"));
        await Eventually(() => ServerSelection.SequenceEqual(["docs/a.md"]), "the server has the selection");

        await Box("docs/a.md").UncheckAsync();
        await Expect(Bar).ToBeHiddenAsync();
        await Eventually(() => ServerSelection.Count == 0, "the server has unselected it");
    }

    [Fact]
    public async Task Locked_entries_have_no_box_that_can_be_ticked()
    {
        await OpenAsync("/mixed");

        await Expect(Page.Locator("tr.entry-row[data-path='mixed/locked.md']")).ToHaveCountAsync(0);
        await Expect(Page.Locator("tr[title='No read access to this file.'] input")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task The_box_above_a_group_selects_all_of_it_then_none()
    {
        await OpenAsync("/mixed");
        ILocator all = Page.Locator(".entry-select-all[data-group='files']");

        await all.CheckAsync();
        await Expect(Page.Locator("#selection-count")).ToHaveTextAsync("3 elements selected");
        await Eventually(() => ServerSelection.Count == 3, "the server has all three");

        await all.ClickAsync();
        await Expect(Bar).ToBeHiddenAsync();
        await Eventually(() => ServerSelection.Count == 0, "the server has none");
    }

    [Fact]
    public async Task Dragging_over_rows_selects_them()
    {
        await OpenAsync("/mixed");
        ILocator rows = Page.Locator("tr.entry-row[data-group='files']");
        LocatorBoundingBoxResult first = (await rows.Nth(0).BoundingBoxAsync())!;
        LocatorBoundingBoxResult third = (await rows.Nth(2).BoundingBoxAsync())!;

        await Page.Mouse.MoveAsync(first.X + first.Width - 20, first.Y + first.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(third.X + third.Width - 20, third.Y + third.Height / 2, new MouseMoveOptions { Steps = 5 });
        await Page.Mouse.UpAsync();

        await Expect(Page.Locator("#selection-count")).ToHaveTextAsync("3 elements selected");
        await Eventually(() => ServerSelection.Count == 3, "the server has all three");
    }

    [Fact]
    public async Task Copying_the_selection_into_another_directory()
    {
        await SelectAsync("docs", "docs/a.md");
        await OpenAsync("/team");

        await Expect(CopyButton).ToBeVisibleAsync();
        await Expect(MoveButton).ToBeVisibleAsync();
        await Expect(DeleteButton).ToBeHiddenAsync();
        await CopyButton.ClickAsync();

        await Expect(Toast).ToContainTextAsync("Copied 1 element here.");
        await Expect(Page.Locator("a[href='/team/a.md']")).ToBeVisibleAsync();
        await Expect(Bar).ToBeHiddenAsync();
        Assert.True(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task Moving_the_selection_with_the_keyboard()
    {
        await SelectAsync("docs", "docs/nested");
        await OpenAsync("/team");

        await Page.Keyboard.PressAsync("Shift+V");

        await Expect(Toast).ToContainTextAsync("Moved 1 element here.");
        await Eventually(() => Tree.Exists("team/nested/b.md") && !Tree.Exists("docs/nested"), "the directory is moved");
    }

    [Fact]
    public async Task Deleting_the_selection_in_its_own_directory_asks_first()
    {
        await SelectAsync("docs", "docs/a.md", "docs/nested");

        await Expect(CopyButton).ToBeHiddenAsync();
        await DeleteButton.ClickAsync();
        await Expect(Page.Locator("#selection-delete-modal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#selection-delete-count")).ToHaveTextAsync("1 directory and 1 file");
        await Page.Locator("#selection-delete-confirm").ClickAsync();

        await Expect(Toast).ToContainTextAsync("Deleted 2 elements.");
        await Eventually(() => Tree.List("docs").Count == 0, "everything is deleted");
    }

    [Fact]
    public async Task Escape_clears_the_selection()
    {
        await SelectAsync("docs", "docs/a.md");
        await Page.Locator("main").ClickAsync(new LocatorClickOptions { Position = new Position { X = 1, Y = 1 } }); // focus off the box

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Bar).ToBeHiddenAsync();
        await Eventually(() => ServerSelection.Count == 0, "the server has cleared it");
    }

    /// <summary>
    /// Right after ticking a box, the box has the focus. It is no text field, so the plain shortcuts of the card (Escape, Delete, V) work.
    /// </summary>
    [Fact]
    public async Task The_shortcuts_of_the_card_work_right_after_ticking_a_box()
    {
        await SelectAsync("docs", "docs/a.md");

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Bar).ToBeHiddenAsync();
    }

    [Fact]
    public async Task What_the_permissions_do_not_allow_is_disabled_with_the_reason()
    {
        await SelectAsync("shelf", "shelf/item.md");
        await OpenAsync("/docs");

        await Expect(MoveButton).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(MoveButton).ToHaveAttributeAsync("title", new Regex("Insufficient permissions to delete or move some of the selected entries"));
        await Expect(CopyButton).ToHaveAttributeAsync("aria-disabled", "false");

        await MoveButton.ClickAsync(new LocatorClickOptions { Force = true }); // as a user would, whatever it looks like
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.True(Tree.Exists("shelf/item.md"));
        Assert.False(Tree.Exists("docs/item.md"));

        await OpenAsync("/shelf");
        await Expect(Page.Locator("#selection-delete")).ToHaveAttributeAsync("aria-disabled", "true");
    }

    [Fact]
    public async Task Nothing_can_be_copied_into_a_directory_that_takes_nothing()
    {
        await SelectAsync("docs", "docs/a.md");
        await OpenAsync("/shelf");

        await Expect(CopyButton).ToHaveAttributeAsync("title", "Copy here: Insufficient permissions to add entries to this directory.");
        await Expect(MoveButton).ToHaveAttributeAsync("aria-disabled", "true");
    }

    [Fact]
    public async Task A_refusal_of_the_server_is_shown_and_the_selection_kept()
    {
        await SelectAsync("docs", "docs/nested");
        await OpenAsync("/team");
        // Since the page was made, something inside became unreadable
        Tree.Config("docs/nested", Cfg.Folder(Cfg.Perms(rwDeny: ["g:team"])));

        await MoveButton.ClickAsync();

        await Expect(Page.Locator(".toast-card[role='alert']")).ToContainTextAsync("No read access to 'docs/nested', so it can't be moved.");
        await Expect(Bar).ToBeVisibleAsync();
        Assert.Equal(["docs/nested"], ServerSelection);
    }

    [Fact]
    public async Task Anonymous_users_have_no_boxes()
    {
        IBrowserContext anonymous = await NewContextAsync(Users.Anonymous);
        IPage page = await anonymous.NewPageAsync();
        await page.GotoAsync("/docs");

        await Assertions.Expect(page.Locator(".entry-select")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#selection-bar")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("text=You are not logged in")).ToBeVisibleAsync();
        await anonymous.DisposeAsync();
    }
}
