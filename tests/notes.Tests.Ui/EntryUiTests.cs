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
        _ = await Page.WaitForFunctionAsync("() => window.editor.getValue() === 'changed'");
        await Expect(SaveButton).ToHaveAttributeAsync("aria-disabled", "false");
    }

    [Fact]
    public async Task Zooming_is_shared_by_both_views_kept_and_stops_at_its_limit()
    {
        const string MonacoFontSize = "() => window.editor.getOption(monaco.editor.EditorOption.fontSize)";
        const string SimpleFontSize = "() => parseFloat(getComputedStyle(document.getElementById('simple-editor')).fontSize)";
        await OpenAsync("/docs/a.md?edit");
        await WaitForEditorAsync();
        double usualMonacoSize = await Page.EvaluateAsync<double>(MonacoFontSize);

        await Page.Locator("#zoom-in-button").ClickAsync();
        Assert.True(await Page.EvaluateAsync<double>(MonacoFontSize) > usualMonacoSize);

        // The simple view has the same zoom
        double usualSimpleSize = await Page.EvaluateAsync<double>("() => parseFloat(getComputedStyle(document.documentElement).fontSize)");
        await Page.Locator("#simple-view-button").ClickAsync();
        Assert.True(await Page.EvaluateAsync<double>(SimpleFontSize) > usualSimpleSize);

        // Kept for the next page, and back to the usual size with Ctrl+0 inside the editor
        await OpenAsync("/docs/a.md?edit");
        Assert.True(await Page.EvaluateAsync<double>(SimpleFontSize) > usualSimpleSize);
        await Page.Locator("#simple-editor").FocusAsync();
        await Page.Keyboard.PressAsync("Control+0");
        Assert.Equal(usualSimpleSize, await Page.EvaluateAsync<double>(SimpleFontSize));

        // Zooming out as far as it goes disables the button, with the reason
        _ = await Page.EvaluateAsync("() => { while (editorZoom.canZoomOut) editorZoom.zoomOut(); }");
        await ExpectDisabledWithReason(Page.Locator("#zoom-out-button"));
        await Expect(Page.Locator("#zoom-in-button")).ToHaveAttributeAsync("aria-disabled", "false");
    }

    [Fact]
    public async Task A_file_alice_may_not_change_is_read_only_and_save_says_why()
    {
        // (Markdown that can't be changed is only shown rendered)
        Tree.Config("readonly", Cfg.Folder(Cfg.Perms(rwReadOnly: ["g:team"])));
        _ = Tree.File("readonly/view.txt", "view");
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
        _ = await page.GotoAsync("/docs/a.md");

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
        _ = Tree.File("docs/evil.md", evil);

        await OpenAsync("/docs/evil.md?edit");
        await WaitForEditorAsync();

        Assert.Equal(evil, await Page.EvaluateAsync<string>("() => window.editor.getValue()"));
        Assert.True(await Page.EvaluateAsync<bool>("() => window.pwned === undefined"));
    }

    // ---- Markdown preview

    [Fact]
    public async Task Html_in_a_note_is_text_in_the_preview_and_never_runs()
    {
        _ = Tree.File("docs/evil.md", "<script>window.pwned = 1</script>\n\n<img src=x onerror=\"window.pwned = 2\">\n\n[go](javascript:window.pwned=3)");

        await OpenAsync("/docs/evil.md");
        await Expect(Page.Locator("#markdown-preview")).ToContainTextAsync("<script>window.pwned = 1</script>");
        await Page.Locator("#markdown-preview").GetByText("go").ClickAsync();
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.True(await Page.EvaluateAsync<bool>("() => window.pwned === undefined"));
    }

    private const string EditHereNote = "# Title\n\nFirst paragraph.\n\n- one\n- two\n\n```csharp\nint x;\n```\n";

    [Fact]
    public async Task Pointing_at_a_part_of_the_text_offers_to_edit_it_where_it_is_written()
    {
        _ = Tree.File("docs/edit-here.md", EditHereNote);
        ILocator button = Page.Locator("#edit-here-button");

        await OpenAsync("/docs/edit-here.md");
        await Expect(button).ToBeHiddenAsync();
        await Page.Locator("#markdown-preview li").Nth(1).HoverAsync();
        await Expect(button).ToBeVisibleAsync();
        await Expect(button).ToHaveAttributeAsync("href", "?edit#line=6&column=1");

        // Left of the text, at the height of the item
        LocatorBoundingBoxResult buttonBox = (await button.BoundingBoxAsync())!;
        LocatorBoundingBoxResult itemBox = (await Page.Locator("#markdown-preview li").Nth(1).BoundingBoxAsync())!;
        LocatorBoundingBoxResult textBox = (await Page.Locator("#markdown-preview").BoundingBoxAsync())!;
        Assert.True(buttonBox.X >= 0 && buttonBox.X + buttonBox.Width <= textBox.X + 1, "the button is in the gutter, on the page");
        Assert.InRange(buttonBox.Y, itemBox.Y - 1, itemBox.Y + itemBox.Height);

        await Page.Locator("#markdown-preview pre").HoverAsync();
        await Expect(button).ToHaveAttributeAsync("href", "?edit#line=8&column=1");

        // Anywhere on the line counts, also far right of the text, and off the lines of the text it goes away at once
        int pageWidth = Page.ViewportSize!.Width;
        await Page.Mouse.MoveAsync(pageWidth - 5, itemBox.Y + itemBox.Height / 2);
        await Expect(button).ToHaveAttributeAsync("href", "?edit#line=6&column=1");
        await Page.Mouse.MoveAsync(pageWidth - 5, 700);
        await Expect(button).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 200 });

        await Page.Locator("#markdown-preview li").Nth(1).HoverAsync();
        await button.ClickAsync();
        await WaitForEditorAsync();
        _ = await Page.WaitForFunctionAsync("() => { const p = window.editor.getPosition(); return p.lineNumber === 6 && p.column === 1; }");
        Assert.DoesNotContain("#", Page.Url);
    }

    [Fact]
    public async Task On_a_phone_tapping_a_part_of_the_text_offers_to_edit_it_in_the_simple_editor()
    {
        _ = Tree.File("docs/edit-here.md", EditHereNote);
        await Page.SetViewportSizeAsync(390, 844);
        ILocator button = Page.Locator("#edit-here-button");

        await OpenAsync("/docs/edit-here.md");
        _ = await Page.EvaluateAsync("() => localStorage.setItem('notes.simpleEditor', 'true')");
        await Page.Locator("#markdown-preview p").First.ClickAsync();
        await Expect(button).ToBeVisibleAsync();
        LocatorBoundingBoxResult buttonBox = (await button.BoundingBoxAsync())!;
        LocatorBoundingBoxResult textBox = (await Page.Locator("#markdown-preview p").First.BoundingBoxAsync())!;
        Assert.True(buttonBox.X >= 0 && buttonBox.X + buttonBox.Width <= textBox.X, "the button is left of the text, on the screen");

        // Tapping elsewhere takes it away
        await Page.Locator("footer").ClickAsync();
        await Expect(button).ToBeHiddenAsync();

        await Page.Locator("#markdown-preview p").First.ClickAsync();
        await button.ClickAsync();
        await Expect(Page.Locator("#simple-editor")).ToBeVisibleAsync();
        _ = await Page.WaitForFunctionAsync("() => document.getElementById('simple-editor').selectionStart === '# Title\\n\\n'.length");
    }

    [Fact]
    public async Task Whoever_may_not_change_a_note_is_offered_to_view_the_source_of_parts_of_it()
    {
        _ = Tree.File("docs/edit-here.md", EditHereNote);
        IBrowserContext bob = await NewContextAsync(Users.Bob);
        IPage page = await bob.NewPageAsync();
        ILocator button = page.Locator("#view-source-here-button");
        _ = await page.GotoAsync("/docs/edit-here.md");

        await Assertions.Expect(page.Locator("#markdown-preview")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#edit-here-button")).ToHaveCountAsync(0);
        await page.Locator("#markdown-preview li").Nth(1).HoverAsync();
        await Assertions.Expect(button).ToBeVisibleAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("href", "?edit#line=6&column=1");

        // The source, read-only, at that place
        await button.ClickAsync();
        _ = await page.WaitForFunctionAsync("() => window.editor && typeof window.editor.getValue === 'function'");
        _ = await page.WaitForFunctionAsync("() => { const p = window.editor.getPosition(); return p.lineNumber === 6 && p.column === 1; }");
        Assert.True(await page.EvaluateAsync<bool>("() => window.editor.getOption(monaco.editor.EditorOption.readOnly)"));
        await bob.DisposeAsync();
    }

    [Fact]
    public async Task Math_and_diagrams_are_rendered()
    {
        _ = Tree.File("docs/math.md", "Inline $a^2$.\n\n$$\n\\frac{1}{2}\n$$\n\n```mermaid\ngraph TD; A-->B\n```");

        await OpenAsync("/docs/math.md");

        await Expect(Page.Locator("#markdown-preview span.math .katex")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#markdown-preview div.math .katex-display")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#markdown-preview .markdown-diagram svg")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Saving_as_pdf_prints_in_the_light_theme_with_the_file_name_as_title_and_puts_both_back()
    {
        _ = Tree.File("docs/diagram.md", "```mermaid\ngraph TD; A-->B\n```");
        await OpenAsync("/docs/diagram.md");
        await Expect(Page.Locator("#markdown-preview .markdown-diagram svg")).ToHaveCountAsync(1);
        if (await Page.EvaluateAsync<string>("() => getTheme()") != "dark")
            await Page.Locator("#theme-toggle").ClickAsync();
        string title = await Page.TitleAsync();

        // The print dialog can't be used from a test: what the page is like when it would open is noted instead
        _ = await Page.EvaluateAsync("""
            () => window.print = () => window.printed = {
                theme: getTheme(),
                title: document.title
            }
            """);
        await Page.Keyboard.PressAsync("Control+p");
        _ = await Page.WaitForFunctionAsync("() => window.printed !== undefined");
        Assert.Equal("light", await Page.EvaluateAsync<string>("() => window.printed.theme"));
        Assert.Equal("diagram", await Page.EvaluateAsync<string>("() => window.printed.title"));

        _ = await Page.EvaluateAsync("() => window.dispatchEvent(new Event('afterprint'))");
        Assert.Equal("dark", await Page.EvaluateAsync<string>("() => getTheme()"));
        Assert.Equal(title, await Page.TitleAsync());
        Assert.Equal("dark", await Page.EvaluateAsync<string>("() => localStorage.getItem('theme')"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Saving_as_pdf_tells_to_turn_off_the_browsers_header_and_footer_only_where_they_can_not_be_replaced(bool canReplace)
    {
        // Firefox, for instance, has no page margin boxes
        if (!canReplace)
            await Page.AddInitScriptAsync("delete window.CSSMarginRule");
        await Page.Clock.InstallAsync(); // holds the frames back, so what happens before the dialog opens can be looked at
        await OpenAsync("/docs/a.md");
        // The print dialog blocks the page, so the hint must already be there when it opens
        _ = await Page.EvaluateAsync("() => window.print = () => window.hintsWhenPrinted = document.querySelectorAll(\".toast-card[data-kind='info']\").length");

        await Page.Locator("#pdf-button").ClickAsync();
        if (!canReplace)
        {
            await Expect(Page.Locator(".toast-card[data-kind='info']")).ToBeVisibleAsync();
            Assert.True(await Page.EvaluateAsync<bool>("() => window.hintsWhenPrinted === undefined"), "the dialog waits until the hint is drawn");
        }
        await Page.Clock.RunForAsync(100);
        _ = await Page.WaitForFunctionAsync("() => window.hintsWhenPrinted !== undefined");

        Assert.Equal(canReplace ? 0 : 1, await Page.EvaluateAsync<int>("() => window.hintsWhenPrinted"));
    }

    [Fact]
    public async Task Tapping_an_abbreviation_shows_what_it_stands_for_and_tapping_elsewhere_hides_it()
    {
        _ = Tree.File("docs/abbreviations.md", "The W3C makes standards.\n\n*[W3C]: World Wide Web Consortium");
        await Page.SetViewportSizeAsync(390, 844);

        await OpenAsync("/docs/abbreviations.md");
        await Page.Locator("#markdown-preview abbr").ClickAsync();
        await Expect(Page.Locator(".tooltip")).ToContainTextAsync("World Wide Web Consortium");

        await Page.Mouse.ClickAsync(5, 400);
        await Expect(Page.Locator(".tooltip")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_diagram_that_can_not_be_drawn_shows_its_text_and_why()
    {
        _ = Tree.File("docs/broken.md", "```mermaid\nthis is no diagram\n```");

        await OpenAsync("/docs/broken.md");

        await Expect(Page.Locator("#markdown-preview [data-diagram-error]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#markdown-preview code.language-mermaid")).ToContainTextAsync("this is no diagram");
    }

    [Fact]
    public async Task A_diagram_can_not_run_scripts()
    {
        _ = Tree.File("docs/evil-diagram.md", "```mermaid\ngraph TD; A[\"<img src=x onerror='window.pwned = 1'>\"]-->B\nclick A call alert()\n```");

        await OpenAsync("/docs/evil-diagram.md");
        await Expect(Page.Locator("#markdown-preview .markdown-diagram")).ToHaveCountAsync(1);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.True(await Page.EvaluateAsync<bool>("() => window.pwned === undefined"));
    }

    [Fact]
    public async Task The_scripts_for_math_and_diagrams_are_only_loaded_where_they_are_used()
    {
        _ = Tree.File("docs/plain.md", "No math, only 5 dollars and `code`.");

        await OpenAsync("/docs/plain.md");
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();

        Assert.True(await Page.EvaluateAsync<bool>("() => window.katex === undefined && window.mermaid === undefined"));
    }

    [Fact]
    public async Task Links_to_other_sites_open_in_a_new_tab_and_links_to_the_notes_do_not()
    {
        _ = Tree.File("docs/links.md", "[out](https://example.com) [in](/docs/a.md)");

        await OpenAsync("/docs/links.md");

        await Expect(Page.Locator("#markdown-preview a[href='https://example.com']")).ToHaveAttributeAsync("target", "_blank");
        await Expect(Page.Locator("#markdown-preview a[href='/docs/a.md']")).Not.ToHaveAttributeAsync("target", new Regex(".*"));
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
    public async Task Markdown_that_can_not_be_changed_leads_to_its_source_read_only()
    {
        await OpenAsync("/mixed/view.md");
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();
        await Expect(Page.Locator("#edit-button")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("e");
        await Expect(Page).ToHaveURLAsync(new Regex("/mixed/view\\.md\\?edit$"));
        await WaitForEditorAsync();
        Assert.True(await Page.EvaluateAsync<bool>("() => window.editor.getOption(monaco.editor.EditorOption.readOnly)"));

        await Page.Locator("#preview-button").ClickAsync();
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Other_text_files_only_have_the_editor()
    {
        _ = Tree.File("docs/plain.txt", "text");

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
        _ = Tree.File("docs/empty.md");
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
        _ = await page.GotoAsync("/");

        // Not pulled here: that would shut down the app the other tests use (see KillSwitchTests of the integration tests)
        await page.Keyboard.PressAsync("Control+Alt+Shift+Delete");
        await Assertions.Expect(page.Locator("#kill-switch-modal")).ToBeVisibleAsync();
        // Bootstrap takes the focus into the modal once it has faded in, and ignores closing it before that
        _ = await page.WaitForFunctionAsync("() => document.getElementById('kill-switch-modal').contains(document.activeElement)");
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
    public async Task Hovering_a_shortcut_shows_it_in_full_and_the_footer_says_so()
    {
        await OpenAsync("/docs");
        ILocator footerHint = Page.Locator("[key-held-text]");

        await Page.Keyboard.DownAsync("ControlRight");
        ILocator hint = Page.Locator(".key-hint").First;
        await hint.HoverAsync();
        await Expect(hint).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("key-hint-expanded"));
        await Expect(footerHint).ToHaveAttributeAsync("data-key-resting-text", new System.Text.RegularExpressions.Regex("."));
        await Page.Keyboard.UpAsync("ControlRight");

        await Expect(footerHint).Not.ToHaveAttributeAsync("data-key-resting-text", new System.Text.RegularExpressions.Regex(".*"));
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
