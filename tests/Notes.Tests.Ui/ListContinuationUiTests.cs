namespace Notes.Tests.Ui;

/// <summary>Enter goes on with a list of a Markdown file (markdown-lists.js), in both editors.</summary>
public sealed class ListContinuationUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    // The simple view (which the browser keeps once chosen), with the cursor at the end of the text
    private async Task OpenSimpleViewAsync(string path, string contents)
    {
        _ = Tree.File(path, contents);
        await OpenAsync("/" + path + "?edit");
        _ = await Page.EvaluateAsync("() => { if (document.getElementById('simple-editor').hidden) toggleSimpleView(); }");
        await Page.Locator("#simple-editor").FocusAsync();
        _ = await Page.EvaluateAsync("() => { const field = document.getElementById('simple-editor'); field.setSelectionRange(field.value.length, field.value.length); }");
    }

    private async Task<string> SimpleValueAsync()
        => await Page.EvaluateAsync<string>("() => document.getElementById('simple-editor').value");

    [Theory]
    [InlineData("  - item", "  - item\n  - next")]
    [InlineData("* item", "* item\n* next")]
    [InlineData("9. item", "9. item\n10. next")]
    [InlineData("1) item", "1) item\n2) next")]
    [InlineData("- [x] done", "- [x] done\n- [ ] next")]
    public async Task Enter_in_a_list_starts_the_next_item(string line, string expected)
    {
        await OpenSimpleViewAsync("docs/list.md", line);

        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("next");

        Assert.Equal(expected, await SimpleValueAsync());
    }

    [Fact]
    public async Task Enter_on_an_empty_item_ends_the_list()
    {
        await OpenSimpleViewAsync("docs/list.md", "- item\n  - ");

        await Page.Keyboard.PressAsync("Enter");

        Assert.Equal("- item\n", await SimpleValueAsync());
    }

    [Fact]
    public async Task Shift_Enter_and_text_that_is_no_list_get_a_plain_line_break()
    {
        await OpenSimpleViewAsync("docs/list.md", "- item");
        await Page.Keyboard.PressAsync("Shift+Enter");
        Assert.Equal("- item\n", await SimpleValueAsync());

        await OpenSimpleViewAsync("docs/text.md", "-not a list");
        await Page.Keyboard.PressAsync("Enter");
        Assert.Equal("-not a list\n", await SimpleValueAsync());
    }

    [Fact]
    public async Task Only_markdown_files_go_on_with_lists()
    {
        await OpenSimpleViewAsync("docs/list.txt", "- item");

        await Page.Keyboard.PressAsync("Enter");

        Assert.Equal("- item\n", await SimpleValueAsync());
    }

    [Fact]
    public async Task The_code_editor_goes_on_with_a_list_too()
    {
        _ = Tree.File("docs/list.md", "  - item");
        await OpenAsync("/docs/list.md?edit");
        await WaitForEditorAsync();
        _ = await Page.EvaluateAsync("() => { window.editor.focus(); window.editor.setPosition({ lineNumber: 1, column: 9 }); }");

        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("next");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.PressAsync("Enter");

        // Monaco's line breaks are those of the system
        Assert.Equal("  - item\n  - next\n", (await Page.EvaluateAsync<string>("() => window.editor.getValue()")).ReplaceLineEndings("\n"));
    }
}
