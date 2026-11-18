using Microsoft.Playwright;

namespace notes.Tests.Ui;

/// <summary>
/// The ways between the rendered markdown and the place in its text: the button that edits a part where it is written
/// (markdown-edit-here.js, editor-start-position.js), and the preview that starts where the editor's cursor was (markdown-start-position.js).
/// </summary>
public sealed class EditHereUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private const string EditHereNote = "# Title\n\nFirst paragraph.\n\n- one\n- two\n\n```csharp\nint x;\n```\n";

    private ILocator EditHereButton => Page.Locator("#edit-here-button");

    // Where on the screen a piece of text of the preview starts: the middle of the left edge of its first character
    private async Task<(float X, float Y)> StartOfTextAsync(string text)
    {
        float[] point = await Page.EvaluateAsync<float[]>(@"text => {
            const walker = document.createTreeWalker(document.getElementById('markdown-preview'), NodeFilter.SHOW_TEXT);
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                const index = node.data.indexOf(text);
                if (index === -1)
                    continue;
                const range = document.createRange();
                range.setStart(node, index);
                range.setEnd(node, index + 1);
                const box = range.getBoundingClientRect();
                return [box.left + 1, box.top + box.height / 2];
            }
            return null;
        }", text);
        return (point[0], point[1]);
    }

    // ---- The button in the gutter

    [Fact]
    public async Task Pointing_at_a_part_of_the_text_offers_to_edit_it_where_it_is_written()
    {
        _ = Tree.File("docs/edit-here.md", EditHereNote);
        ILocator button = EditHereButton;

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
    public async Task Between_the_items_of_a_list_the_button_stays_with_the_item_above()
    {
        // Items apart by blank lines are lists of their own (MarkdownLists): the gap between two items is the margin of the list above
        _ = Tree.File("docs/loose-list.md", "- one\n\n- two\n\n- three\n");
        ILocator button = EditHereButton;
        ILocator items = Page.Locator("#markdown-preview li");

        await OpenAsync("/docs/loose-list.md");
        // The second item, not the first: the list starts on the same line as its first item
        LocatorBoundingBoxResult secondItem = (await items.Nth(1).BoundingBoxAsync())!;
        LocatorBoundingBoxResult thirdItem = (await items.Nth(2).BoundingBoxAsync())!;
        Assert.True(thirdItem.Y > secondItem.Y + secondItem.Height, "there is a gap between the items");

        await Page.Mouse.MoveAsync(secondItem.X + 10, (secondItem.Y + secondItem.Height + thirdItem.Y) / 2);
        await Expect(button).ToBeVisibleAsync();
        // The second item, as when it is pointed at itself (it holds no paragraph of its own)
        await Expect(button).ToHaveAttributeAsync("href", "?edit#line=3&column=1");
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

    // ---- At a character: with Alt, and on a phone

    [Fact]
    public async Task With_alt_held_the_button_is_where_the_mouse_is_and_leads_to_that_character()
    {
        // The character is found again behind markup: the bold, and a link whose address has the same word in it
        _ = Tree.File("docs/alt.md", "# Title\n\nSome **bold** and [a link](https://example.com/here) here.\n");
        await OpenAsync("/docs/alt.md");
        (float x, float y) = await StartOfTextAsync("here.");

        await Page.Mouse.MoveAsync(x, y);
        await Page.Keyboard.DownAsync("Alt");
        await Expect(EditHereButton).ToBeVisibleAsync();
        LocatorBoundingBoxResult buttonBox = (await EditHereButton.BoundingBoxAsync())!;
        Assert.InRange(x, buttonBox.X, buttonBox.X + buttonBox.Width);
        Assert.InRange(y, buttonBox.Y, buttonBox.Y + buttonBox.Height);
        await Expect(Page.Locator("html")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("has-edit-here-at-pointer"));

        // Off the text there is nothing to lead to
        await Page.Mouse.MoveAsync(Page.ViewportSize!.Width - 5, y);
        await Expect(EditHereButton).ToBeHiddenAsync();
        await Expect(Page.Locator("html")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("has-edit-here-at-pointer"));

        // Letting go of Alt brings back the button in the gutter
        await Page.Mouse.MoveAsync(x, y);
        await Page.Keyboard.UpAsync("Alt");
        await Expect(EditHereButton).ToHaveAttributeAsync("href", "?edit#line=3&column=1");

        // The click goes through to the text, and opens the editor right before "here."
        await Page.Keyboard.DownAsync("Alt");
        await Page.Mouse.ClickAsync(x, y);
        await Page.Keyboard.UpAsync("Alt");
        await WaitForEditorAsync();
        int column = "Some **bold** and [a link](https://example.com/here) ".Length + 1;
        _ = await Page.WaitForFunctionAsync("column => { const p = window.editor.getPosition(); return p.lineNumber === 3 && p.column === column; }", column);
    }

    [Fact]
    public async Task On_a_phone_tapping_the_text_shows_the_button_over_it_which_leads_to_the_character_tapped()
    {
        _ = Tree.File("docs/edit-here.md", EditHereNote);
        await Page.SetViewportSizeAsync(390, 844);
        ILocator button = EditHereButton;

        await OpenAsync("/docs/edit-here.md");
        _ = await Page.EvaluateAsync("() => localStorage.setItem('notes.simpleEditor', 'true')");
        LocatorBoundingBoxResult textBox = (await Page.Locator("#markdown-preview").BoundingBoxAsync())!;
        (float x, float y) = await StartOfTextAsync("paragraph");

        // Over the text, not in a gutter, and the text has no room left for one
        await Page.Mouse.ClickAsync(x, y);
        await Expect(button).ToBeVisibleAsync();
        LocatorBoundingBoxResult buttonBox = (await button.BoundingBoxAsync())!;
        Assert.True(buttonBox.X >= textBox.X && buttonBox.X + buttonBox.Width <= textBox.X + textBox.Width, "the button is over the text");
        Assert.InRange(x, buttonBox.X, buttonBox.X + buttonBox.Width);

        // Tapping the same part again takes it away, and so does tapping elsewhere
        await Page.Mouse.ClickAsync(x, y);
        await Expect(button).ToBeHiddenAsync();
        await Page.Mouse.ClickAsync(x, y);
        await Expect(button).ToBeVisibleAsync();
        await Page.Locator("footer").ClickAsync();
        await Expect(button).ToBeHiddenAsync();

        await Page.Mouse.ClickAsync(x, y);
        await button.ClickAsync();
        await Expect(Page.Locator("#simple-editor")).ToBeVisibleAsync();
        _ = await Page.WaitForFunctionAsync("() => document.getElementById('simple-editor').selectionStart === '# Title\\n\\nFirst '.length");
    }

    // ---- From the editor to the preview

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_preview_starts_where_the_cursor_of_the_editor_was(bool simpleView)
    {
        // Long enough that the place is far below the top
        string text = string.Concat(Enumerable.Range(1, 60).Select(i => $"Paragraph {i}.\n\n"));
        _ = Tree.File("docs/long.md", text);
        int line = 2 * 50 - 1; // "Paragraph 50."

        await OpenAsync("/docs/long.md?edit");
        if (simpleView)
        {
            _ = await Page.EvaluateAsync("() => toggleSimpleView()");
            int offset = text.IndexOf("Paragraph 50.", StringComparison.Ordinal) + 3;
            _ = await Page.EvaluateAsync("offset => { const field = document.getElementById('simple-editor'); field.focus(); field.setSelectionRange(offset, offset); }", offset);
        }
        else
        {
            await WaitForEditorAsync();
            _ = await Page.EvaluateAsync("line => window.editor.setPosition({ lineNumber: line, column: 4 })", line);
        }
        await Page.Locator("#preview-button").ClickAsync();

        ILocator paragraph = Page.Locator($"#markdown-preview p[data-source-line='{line}']");
        await Expect(paragraph).ToBeInViewportAsync();
        Assert.True(await Page.EvaluateAsync<double>("() => window.scrollY") > 0, "the preview was scrolled to the place");
        Assert.DoesNotContain("#", Page.Url);
    }

    [Fact]
    public async Task The_preview_starts_at_the_top_without_a_place()
    {
        _ = Tree.File("docs/long.md", string.Concat(Enumerable.Range(1, 60).Select(i => $"Paragraph {i}.\n\n")));
        await OpenAsync("/docs/long.md");
        Assert.Equal(0, await Page.EvaluateAsync<double>("() => window.scrollY"));
    }
}
