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
        await OpenAsync("/docs/a.md?edit");
        await WaitForEditorAsync();
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "true");

        await Page.Keyboard.PressAsync("Control+End");
        await Page.Keyboard.TypeAsync(" and more");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "false");
        await Page.Keyboard.PressAsync("Control+s");

        await Eventually(() => Tree.Read("docs/a.md") == "a and more", "the file is saved");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "true");
        await ExpectDisabledWithReason(SaveButton);
    }

    [Fact]
    public async Task The_simple_view_is_a_text_field_that_is_remembered_and_saves()
    {
        await OpenAsync("/docs/a.md?edit");
        await WaitForEditorAsync();
        await Expect(Page.Locator("#simple-editor")).ToBeHiddenAsync();

        // Monaco's contents come along, and so do the changes made in the simple view
        await Page.Locator("#simple-view-button").ClickAsync();
        await Expect(Page.Locator("#editor")).ToBeHiddenAsync();
        await Expect(Page.Locator("#simple-editor")).ToHaveValueAsync("a");
        await Expect(Page.Locator("#simple-view-button")).ToHaveAttributeAsync("data-leads-to", "code");
        await Page.Locator("#simple-editor").FillAsync("a and more");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "false");
        await SaveButton.ClickAsync();
        await Eventually(() => Tree.Read("docs/a.md") == "a and more", "the file is saved");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "true");

        // The choice is kept for the next page
        await OpenAsync("/docs/a.md?edit");
        await Expect(Page.Locator("#simple-editor")).ToHaveValueAsync("a and more");
        await Expect(Page.Locator("#editor")).ToBeHiddenAsync();

        await Page.Locator("#simple-editor").FillAsync("changed");
        await Page.Locator("#simple-view-button").ClickAsync();
        await WaitForEditorAsync();
        await Expect(Page.Locator("#simple-editor")).ToBeHiddenAsync();
        await Expect(Page.Locator("#simple-view-button")).ToHaveAttributeAsync("data-leads-to", "simple");
        await Page.WaitForFunctionAsync("() => window.editor.getValue() === 'changed'");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "false");
    }

    [Fact]
    public async Task A_file_alice_may_not_change_is_read_only_and_save_says_why()
    {
        // (Markdown that can't be changed is only shown rendered)
        Tree.Config("readonly", Cfg.Folder(Cfg.Perms(rwReadOnly: ["g:team"])));
        Tree.File("readonly/view.txt", "view");
        await OpenAsync("/readonly/view.txt");
        await WaitForEditorAsync();

        await ExpectDisabledWithReason(SaveButton);
        await Page.Keyboard.TypeAsync("changed");
        await Page.Keyboard.PressAsync("Control+s");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal("view", await Page.EvaluateAsync<string>("() => window.editor.getValue()"));
        Assert.Equal("view", Tree.Read("readonly/view.txt"));
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

        await OpenAsync("/docs/evil.md?edit");
        await WaitForEditorAsync();

        Assert.Equal(evil, await Page.EvaluateAsync<string>("() => window.editor.getValue()"));
        Assert.True(await Page.EvaluateAsync<bool>("() => window.pwned === undefined"));
    }

    // ---- Markdown preview

    [Fact]
    public async Task Html_in_a_note_is_text_in_the_preview_and_never_runs()
    {
        Tree.File("docs/evil.md", "<script>window.pwned = 1</script>\n\n<img src=x onerror=\"window.pwned = 2\">\n\n[go](javascript:window.pwned=3)");

        await OpenAsync("/docs/evil.md");
        await Expect(Page.Locator("#markdown-preview")).ToContainTextAsync("<script>window.pwned = 1</script>");
        await Page.Locator("#markdown-preview").GetByText("go").ClickAsync();
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.True(await Page.EvaluateAsync<bool>("() => window.pwned === undefined"));
    }

    [Fact]
    public async Task Markdown_opens_rendered_and_e_and_p_go_between_it_and_the_editor()
    {
        await OpenAsync("/docs/a.md");
        await Expect(Page.Locator("#markdown-preview")).ToContainTextAsync("a");
        await Expect(Page.Locator("#editor")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("e");
        await Expect(Page).ToHaveURLAsync(new Regex("/docs/a\\.md\\?edit$"));
        await WaitForEditorAsync();
        Assert.Equal("a", await Page.EvaluateAsync<string>("() => window.editor.getValue()"));

        // While the editor has the focus the key is typed. Escape leaves it.
        await Page.Keyboard.PressAsync("p");
        Assert.Equal("pa", await Page.EvaluateAsync<string>("() => window.editor.getValue()"));
        await Page.Keyboard.PressAsync("Escape");
        await Page.Keyboard.PressAsync("p");
        await Expect(Page).ToHaveURLAsync(new Regex("/docs/a\\.md\\?$"));
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_buttons_of_the_toolbar_go_between_preview_and_editor()
    {
        await OpenAsync("/docs/a.md");
        await Expect(Page.Locator("#save-button")).ToHaveCountAsync(0);
        await Page.Locator("#edit-button").ClickAsync();
        await WaitForEditorAsync();
        await Expect(Page.Locator("#save-button")).ToHaveCountAsync(1);

        await Page.Locator("#preview-button").ClickAsync();
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Markdown_that_can_not_be_changed_has_no_way_to_the_editor()
    {
        await OpenAsync("/mixed/view.md");
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();
        await Expect(Page.Locator("#edit-button")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("e");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("edit", new Uri(Page.Url).Query);

        await OpenAsync("/mixed/view.md?edit");
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();
        await Expect(Page.Locator("#editor")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Other_text_files_only_have_the_editor()
    {
        Tree.File("docs/plain.txt", "text");

        await OpenAsync("/docs/plain.txt");
        await WaitForEditorAsync();

        await Expect(Page.Locator("#edit-button")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#preview-button")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#markdown-preview")).ToHaveCountAsync(0);
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
        await Expect(ToastOf("success")).ToBeVisibleAsync();
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

        await Expect(ToastOf("danger")).ToBeVisibleAsync();
        Assert.True(Tree.Exists("docs/a.md"));
        await Expect(Page).ToHaveURLAsync(new Regex("/docs/a\\.md$"));
    }

    [Fact]
    public async Task Unsaved_changes_stop_a_rename()
    {
        await OpenAsync("/docs/a.md?edit");
        await WaitForEditorAsync();
        await Page.Keyboard.TypeAsync("x");

        await Page.Locator("#rename-input").FillAsync("z.md");
        await Page.Locator("#rename-input").PressAsync("Enter");

        await Expect(ToastOf("warning")).ToBeVisibleAsync();
        Assert.True(Tree.Exists("docs/a.md"));
    }

    [Fact]
    public async Task What_can_not_be_renamed_is_read_only_with_the_reason()
    {
        await OpenAsync("/shelf/item.md");

        await Expect(Page.Locator("#rename-input")).ToHaveAttributeAsync("readonly", "");
        await ExpectDisabledWithReason(Page.Locator("#rename-input"));
    }

    // ---- Delete

    [Fact]
    public async Task An_empty_file_is_deleted_without_asking()
    {
        Tree.File("docs/empty.md");
        await OpenAsync("/docs/empty.md");

        await Page.Locator("#delete-button").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/docs$"));
        await Expect(ToastOf("success")).ToBeVisibleAsync();
        Assert.False(Tree.Exists("docs/empty.md"));
    }

    [Fact]
    public async Task A_file_with_contents_asks_first()
    {
        await OpenAsync("/docs/a.md");

        await Page.Locator("#delete-button").ClickAsync();
        await Expect(Page.Locator("#delete-modal")).ToBeVisibleAsync();
        Assert.True(Tree.Exists("docs/a.md"));
        await Page.Locator("#delete-confirm").ClickAsync();

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
        await Page.Locator("#delete-confirm").ClickAsync();

        // Names what is in the way
        await Expect(ToastOf("danger")).ToContainTextAsync("docs/nested");
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
        await ExpectDisabledWithReason(delete);
        await delete.ClickAsync(new LocatorClickOptions { Force = true }); // as a user would, whatever it looks like
        await Page.Keyboard.PressAsync("Delete");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await Expect(Page.Locator("#delete-modal")).ToBeHiddenAsync();
        Assert.True(Tree.Exists("drop"));
    }

    [Fact]
    public async Task The_kill_switch_asks_first_by_shortcut_too()
    {
        IBrowserContext admin = await NewContextAsync(Users.Admin);
        IPage page = await admin.NewPageAsync();
        await page.GotoAsync("/");

        // Not pulled here: that would shut down the app the other tests use (see KillSwitchTests of the integration tests)
        await page.Keyboard.PressAsync("Control+Alt+Shift+Delete");
        await Assertions.Expect(page.Locator("#kill-switch-modal")).ToBeVisibleAsync();
        // Bootstrap takes the focus into the modal once it has faded in, and ignores closing it before that
        await page.WaitForFunctionAsync("() => document.getElementById('kill-switch-modal').contains(document.activeElement)");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#kill-switch-modal")).ToBeHiddenAsync();
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
        await OpenAsync("/docs/a.md?edit");

        await WaitForEditorAsync();

        // Every shortcut, whichever they are: one announced combination per combination of the key attribute, with the modifiers written out
        string[] mismatches = await Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('[key]')]
                .filter(el => {
                    const specs = el.getAttribute('key').split(/\s+/).filter(Boolean);
                    const announced = (el.getAttribute('aria-keyshortcuts') || '').split(' ').filter(Boolean);
                    return announced.length !== specs.length
                        || specs.some((spec, i) => spec.length > 1 && spec[0] === '^' && !announced[i].startsWith('Control+'));
                })
                .map(el => el.id || el.outerHTML.slice(0, 80))
            """);
        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task The_notice_of_a_forced_logout_is_shown_once()
    {
        await Context.AddCookiesAsync([new Cookie { Name = ".notes.notice", Value = "ended", Url = Fixture.BaseUrl }]);

        await OpenAsync("/docs");
        await Expect(Page.Locator("#session-notice")).ToBeVisibleAsync();

        await OpenAsync("/docs");
        await Expect(Page.Locator("#session-notice")).ToHaveCountAsync(0);
    }
}
