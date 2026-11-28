using Microsoft.Playwright;

namespace Notes.Tests.Ui;

/// <summary>What helps typing in the editor of a file: the indent buttons of the simple view (text-area-indent.ts), and the code editor's suggestions.</summary>
public sealed class EditorTypingUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator SimpleEditor => Page.Locator("#simple-editor");
    private ILocator IndentButton => Page.Locator("#indent-button");
    private ILocator OutdentButton => Page.Locator("#outdent-button");

    private async Task SelectAsync(int start, int end)
        => _ = await Page.EvaluateAsync("([start, end]) => document.getElementById('simple-editor').setSelectionRange(start, end)", new[] { start, end });

    private async Task<int[]> SelectionAsync()
        => await Page.EvaluateAsync<int[]>("() => { const field = document.getElementById('simple-editor'); return [field.selectionStart, field.selectionEnd]; }");

    [Fact]
    public async Task The_indent_buttons_are_only_there_in_the_simple_view()
    {
        _ = Tree.File("docs/indent.md", "text\n");
        await OpenAsync("/docs/indent.md?edit");
        await WaitForEditorAsync();
        await Expect(IndentButton).ToBeHiddenAsync();
        await Expect(OutdentButton).ToBeHiddenAsync();

        await Page.Locator("#simple-view-button").ClickAsync();
        await Expect(IndentButton).ToBeVisibleAsync();
        await Expect(OutdentButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Indenting_at_the_cursor_goes_to_the_next_tab_stop_and_keeps_the_focus()
    {
        _ = Tree.File("docs/indent.md", "ab\n");
        await OpenAsync("/docs/indent.md?edit");
        _ = await Page.EvaluateAsync("() => toggleSimpleView()");
        await SimpleEditor.FocusAsync();
        await SelectAsync(1, 1);

        await IndentButton.ClickAsync();
        await Expect(SimpleEditor).ToHaveValueAsync("a   b\n");
        Assert.Equal(new[] { 4, 4 }, await SelectionAsync());
        Assert.True(await Page.EvaluateAsync<bool>("() => document.activeElement === document.getElementById('simple-editor')"), "the field kept the focus");

        // Outdenting takes indents from the start of the line only (as Shift+Tab in the code editor), here two spaces back to the tab stop
        await OutdentButton.ClickAsync();
        await Expect(SimpleEditor).ToHaveValueAsync("a   b\n");
        _ = await Page.EvaluateAsync("() => { const field = document.getElementById('simple-editor'); field.value = '      ab\\n'; field.setSelectionRange(7, 7); }");
        await OutdentButton.ClickAsync();
        await Expect(SimpleEditor).ToHaveValueAsync("    ab\n");
        Assert.Equal(new[] { 5, 5 }, await SelectionAsync());
    }

    [Fact]
    public async Task Indenting_a_selection_over_several_lines_indents_each_of_them()
    {
        _ = Tree.File("docs/indent.md", "one\ntwo\n\nthree\n");
        await OpenAsync("/docs/indent.md?edit");
        _ = await Page.EvaluateAsync("() => toggleSimpleView()");
        await SimpleEditor.FocusAsync();
        await SelectAsync(1, "one\ntwo\n\nth".Length);

        await IndentButton.ClickAsync();
        await Expect(SimpleEditor).ToHaveValueAsync("    one\n    two\n\n    three\n");
        Assert.Equal(new[] { 5, "    one\n    two\n\n    th".Length }, await SelectionAsync());
        await Expect(Page.Locator("#save-button")).ToHaveAttributeAsync("data-disabled-reason", "");

        await OutdentButton.ClickAsync();
        await Expect(SimpleEditor).ToHaveValueAsync("one\ntwo\n\nthree\n");
    }

    [Fact]
    public async Task A_text_indented_with_tabs_is_indented_with_tabs()
    {
        _ = Tree.File("docs/indent.md", "\tone\ntwo\n");
        await OpenAsync("/docs/indent.md?edit");
        _ = await Page.EvaluateAsync("() => toggleSimpleView()");
        await SimpleEditor.FocusAsync();
        await SelectAsync("\tone\n".Length, "\tone\n".Length);

        await IndentButton.ClickAsync();
        await Expect(SimpleEditor).ToHaveValueAsync("\tone\n\ttwo\n");
    }

    [Theory]
    [InlineData("docs/prose.md", false)]
    [InlineData("docs/prose.txt", false)]
    [InlineData("docs/data.json", true)]
    public async Task Words_are_only_suggested_where_the_text_is_not_prose(string path, bool suggestsWords)
    {
        _ = Tree.File(path, "{}");
        await OpenAsync("/" + path + "?edit");
        await WaitForEditorAsync();
        string setting = await Page.EvaluateAsync<string>("() => String(window.editor.getRawOptions().wordBasedSuggestions)");
        Assert.Equal(suggestsWords, setting != "off");
    }
}
