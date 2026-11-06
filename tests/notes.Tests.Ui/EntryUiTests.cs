using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace notes.Tests.Ui;

/// <summary>The scripts of a single entry: the editor and saving, renaming, deleting, and the disabled buttons and shortcuts (site.js, shortcuts.js).</summary>
public sealed class EntryUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator SaveButton => Page.Locator("#save-button");

    // ---- Editor

    [Fact]
    public async Task Typing_and_saving_with_ctrl_s()
    {
        await OpenAsync("/docs/a.md");
        await WaitForEditorAsync();
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "true");

        await Page.Keyboard.PressAsync("Control+End");
        await Page.Keyboard.TypeAsync(" and more");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "false");
        await Page.Keyboard.PressAsync("Control+s");

        await Eventually(() => Tree.Read("docs/a.md") == "a and more", "the file is saved");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(SaveButton).ToHaveAttributeAsync("title", "Save: There is nothing to save.");
    }

    [Fact]
    public async Task A_file_alice_may_not_change_is_read_only_and_save_says_why()
    {
        await OpenAsync("/mixed/view.md");
        await WaitForEditorAsync();

        await Expect(SaveButton).ToHaveAttributeAsync("title", "Save: Insufficient permissions to change this file.");
        await Page.Keyboard.TypeAsync("changed");
        await Page.Keyboard.PressAsync("Control+s");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal("view", await Page.EvaluateAsync<string>("() => window.editor.getValue()"));
        Assert.Equal("view", Tree.Read("mixed/view.md"));
    }

    [Fact]
    public async Task Bob_gets_no_buttons_that_change_things()
    {
        IBrowserContext bob = await NewContextAsync(Users.Bob);
        IPage page = await bob.NewPageAsync();
        await page.GotoAsync("/docs/a.md");

        await Assertions.Expect(page.Locator("#download-button")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#save-button")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#delete-button")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#rename-input")).ToHaveAttributeAsync("readonly", "");
        await bob.DisposeAsync();
    }

    [Fact]
    public async Task Html_in_a_note_is_text_in_the_editor_and_never_runs()
    {
        const string evil = "</script><script>window.pwned = 1</script><img src=x onerror=\"window.pwned = 2\">";
        Tree.File("docs/evil.md", evil);

        await OpenAsync("/docs/evil.md");
        await WaitForEditorAsync();

        Assert.Equal(evil, await Page.EvaluateAsync<string>("() => window.editor.getValue()"));
        Assert.True(await Page.EvaluateAsync<bool>("() => window.pwned === undefined"));
    }

    // ---- Rename

    [Fact]
    public async Task Renaming_with_enter_goes_to_the_new_name_and_says_so()
    {
        await OpenAsync("/docs/a.md");
        ILocator input = Page.Locator("#rename-input");

        await input.FillAsync("z.md");
        await Expect(Page.Locator("#rename-confirm")).ToBeVisibleAsync();
        await input.PressAsync("Enter");

        await Expect(Page).ToHaveURLAsync(new Regex("/docs/z\\.md$"));
        await Expect(Toast).ToContainTextAsync("Renamed to z.md.");
        Assert.True(Tree.Exists("docs/z.md"));
    }

    [Fact]
    public async Task Escape_resets_the_name()
    {
        await OpenAsync("/docs/a.md");
        ILocator input = Page.Locator("#rename-input");

        await input.FillAsync("other.md");
        await input.PressAsync("Escape");

        await Expect(input).ToHaveValueAsync("a.md");
        await Expect(Page.Locator("#rename-confirm")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task A_refused_rename_shows_what_the_server_says()
    {
        await OpenAsync("/docs/a.md");

        await Page.Locator("#rename-input").FillAsync("nested");
        await Page.Locator("#rename-input").PressAsync("Enter");

        await Expect(Page.Locator(".toast-card[role='alert']")).ToContainTextAsync("A file or directory with that name already exists.");
        await Expect(Page).ToHaveURLAsync(new Regex("/docs/a\\.md$"));
    }

    [Fact]
    public async Task Unsaved_changes_stop_a_rename()
    {
        await OpenAsync("/docs/a.md");
        await WaitForEditorAsync();
        await Page.Keyboard.TypeAsync("x");

        await Page.Locator("#rename-input").FillAsync("z.md");
        await Page.Locator("#rename-input").PressAsync("Enter");

        await Expect(Toast).ToContainTextAsync("The file has unsaved changes.");
        Assert.True(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task What_can_not_be_renamed_is_read_only_with_the_reason()
    {
        await OpenAsync("/shelf/item.md");

        await Expect(Page.Locator("#rename-input")).ToHaveAttributeAsync("readonly", "");
        await Expect(Page.Locator("#rename-input")).ToHaveAttributeAsync("title", new Regex("^Rename: Insufficient permissions to delete or move"));
    }

    // ---- Delete

    [Fact]
    public async Task An_empty_file_is_deleted_without_asking()
    {
        Tree.File("docs/empty.md");
        await OpenAsync("/docs/empty.md");
        await WaitForEditorAsync();

        await Page.Locator("#delete-button").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/docs$"));
        await Expect(Toast).ToContainTextAsync("Deleted file.");
        Assert.False(Tree.Exists("docs/empty.md"));
    }

    [Fact]
    public async Task A_file_with_contents_asks_first()
    {
        await OpenAsync("/docs/a.md");
        await WaitForEditorAsync();

        await Page.Locator("#delete-button").ClickAsync();
        await Expect(Page.Locator("#delete-modal")).ToBeVisibleAsync();
        Assert.True(Tree.Exists("docs/a.md"));
        await Page.Locator("#delete-modal .btn-danger").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/docs$"));
        Assert.False(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task A_refused_delete_shows_what_the_server_says()
    {
        await OpenAsync("/docs");
        // Since the page was made, something inside became unreadable
        Tree.Config("docs/nested", Cfg.Folder(Cfg.Perms(rwDeny: ["g:team"])));

        await Page.Locator("#delete-button").ClickAsync();
        await Page.Locator("#delete-modal .btn-danger").ClickAsync();

        await Expect(Page.Locator(".toast-card[role='alert']")).ToContainTextAsync("No read access to 'docs/nested'");
        await Expect(Page.Locator("#delete-button")).ToHaveAttributeAsync("aria-disabled", "false");
        Assert.True(Tree.Exists("docs/nested"));
    }

    // ---- Disabled buttons (site.js) and shortcuts (shortcuts.js)

    [Fact]
    public async Task A_disabled_button_says_why_and_does_nothing_when_clicked()
    {
        await OpenAsync("/drop");
        ILocator delete = Page.Locator("#delete-button");

        await Expect(delete).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(delete).ToHaveAttributeAsync("title", "Delete this directory: Insufficient permissions to delete or move 'drop'.");
        await delete.ClickAsync(new LocatorClickOptions { Force = true }); // as a user would, whatever it looks like
        await Page.Keyboard.PressAsync("Delete");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await Expect(Page.Locator("#delete-modal")).ToBeHiddenAsync();
        Assert.True(Tree.Exists("drop"));
    }

    [Fact]
    public async Task A_shortcut_clicks_its_button()
    {
        await OpenAsync("/docs");

        await Page.Keyboard.PressAsync("n"); // Create subdirectory

        await Expect(Page).ToHaveURLAsync(new Regex("/docs/\\d{4}-\\d{2}-\\d{2}_\\d{2}-\\d{2}$"));
        Assert.Equal(2, Directory.GetDirectories(Tree.Physical("docs")).Length);
    }

    [Fact]
    public async Task Plain_shortcuts_do_not_fire_while_typing()
    {
        await OpenAsync("/docs/a.md");

        await Page.Locator("#rename-input").FillAsync("");
        await Page.Locator("#rename-input").PressSequentiallyAsync("new name");

        await Expect(Page.Locator("#rename-input")).ToHaveValueAsync("new name");
        Assert.Single(Directory.GetDirectories(Tree.Physical("docs")));
    }

    [Fact]
    public async Task Holding_right_control_shows_the_shortcuts()
    {
        await OpenAsync("/docs");

        await Page.Keyboard.DownAsync("ControlRight");
        await Expect(Page.Locator(".key-hint").First).ToBeVisibleAsync();
        await Page.Keyboard.UpAsync("ControlRight");

        await Expect(Page.Locator(".key-hint")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Shortcuts_are_announced_to_assistive_technology()
    {
        await OpenAsync("/docs/a.md");

        await Expect(Page.Locator("#download-button")).ToHaveAttributeAsync("aria-keyshortcuts", "Alt+PageDown");
        await Expect(Page.Locator("#save-button")).ToHaveAttributeAsync("aria-keyshortcuts", "Control+S");
    }

    [Fact]
    public async Task The_notice_of_a_forced_logout_is_shown_once()
    {
        await Context.AddCookiesAsync([new Cookie { Name = ".notes.notice", Value = "ended", Url = Fixture.BaseUrl }]);

        await OpenAsync("/docs");
        await Expect(Page.Locator("text=Your session has ended")).ToBeVisibleAsync();

        await OpenAsync("/docs");
        await Expect(Page.Locator("text=Your session has ended")).ToHaveCountAsync(0);
    }
}
