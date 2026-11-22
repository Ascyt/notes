using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Notes.Tests.Ui;

/// <summary>Putting a file into a markdown file (_InsertMedia.cshtml): the toolbar's button, and files pasted or dropped into the editors.</summary>
public sealed partial class InsertMediaUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator Modal => Page.Locator("#insert-media-modal");
    private ILocator PathField => Page.Locator("#insert-media-path");
    private ILocator SimpleEditor => Page.Locator("#simple-editor");

    private Task<string> MonacoValueAsync() => Page.EvaluateAsync<string>("() => window.editor.getValue()");

    /// <summary>Sends a file of the type to the element as the event (paste or drop), as the browser would.</summary>
    private Task SendFileAsync(string selector, string eventName, string name, string type)
        => Page.EvaluateAsync("""
            ([selector, eventName, name, type]) => {
                const transfer = new DataTransfer();
                transfer.items.add(new File(['bytes'], name, { type }));
                const element = document.querySelector(selector);
                const box = element.getBoundingClientRect();
                const point = { bubbles: true, cancelable: true, clientX: box.left + 5, clientY: box.top + 5 };
                element.dispatchEvent(eventName === 'paste'
                    ? new ClipboardEvent('paste', { ...point, clipboardData: transfer })
                    : new DragEvent(eventName, { ...point, dataTransfer: transfer }));
            }
            """, new[] { selector, eventName, name, type });

    [Fact]
    public async Task The_button_uploads_the_file_next_to_the_note_and_links_it_at_the_cursor()
    {
        _ = Tree.File("docs/note.md", "Text\n");
        await OpenAsync("/docs/note.md?edit");
        await WaitForEditorAsync();

        await Page.Locator("#insert-media-button").ClickAsync();
        await Expect(Modal).ToBeVisibleAsync();
        await Page.Locator("#insert-media-file").SetInputFilesAsync([new FilePayload { Name = "photo.png", MimeType = "image/png", Buffer = [1, 2, 3] }]);
        await Expect(PathField).ToHaveValueAsync("./assets/photo.png");
        await Page.Locator("#insert-media-confirm").ClickAsync();

        await Expect(Modal).ToBeHiddenAsync();
        Assert.Contains("![photo](./assets/photo.png)", await MonacoValueAsync());
        Assert.True(Tree.IsFile("docs/assets/photo.png"));
    }

    [Fact]
    public async Task A_pasted_image_is_named_after_the_time()
    {
        _ = Tree.File("docs/paste.md", "");
        await OpenAsync("/docs/paste.md?edit");
        _ = await Page.EvaluateAsync("() => toggleSimpleView()");

        await SendFileAsync("#simple-editor", "paste", "image.png", "image/png");
        await Expect(Modal).ToBeVisibleAsync();
        await Expect(PathField).ToHaveValueAsync(TimeNamePattern());
        await Page.Locator("#insert-media-confirm").ClickAsync();

        await Expect(SimpleEditor).ToHaveValueAsync(new Regex(@"^!\[\d{4}-\d\d-\d\d_\d\d-\d\d\]\(\./assets/\d{4}-\d\d-\d\d_\d\d-\d\d\.png\)$"));
        Assert.Single(Directory.GetFiles(Tree.Physical("docs/assets")));
    }

    [Fact]
    public async Task A_dropped_video_goes_where_the_path_says_and_is_shown_by_the_preview()
    {
        _ = Tree.File("docs/drop.md", "");
        await OpenAsync("/docs/drop.md?edit");
        await WaitForEditorAsync();

        await SendFileAsync("#editor", "drop", "clip.mp4", "video/mp4");
        await Expect(Modal).ToBeVisibleAsync();
        await PathField.FillAsync("../media/clips/clip.mp4");
        await Page.Locator("#insert-media-confirm").ClickAsync();
        await Expect(Modal).ToBeHiddenAsync();

        Assert.Equal("![clip](../media/clips/clip.mp4)", await MonacoValueAsync());
        Assert.True(Tree.IsFile("media/clips/clip.mp4"));
        await Page.Locator("#save-button").ClickAsync();
        await Eventually(() => Tree.Read("docs/drop.md") != "", "the note is saved");
        await OpenAsync("/docs/drop.md");
        await Expect(Page.Locator("#markdown-preview video")).ToHaveAttributeAsync("src", "../media/clips/clip.mp4?raw");
    }

    [Fact]
    public async Task Any_other_file_is_linked()
    {
        _ = Tree.File("docs/other.md", "");
        await OpenAsync("/docs/other.md?edit");
        await WaitForEditorAsync();

        await SendFileAsync("#editor", "drop", "notes (draft).zip", "application/zip");
        await Page.Locator("#insert-media-confirm").ClickAsync();
        await Expect(Modal).ToBeHiddenAsync();

        Assert.Equal("[notes (draft)](./assets/notes%20%28draft%29.zip)", await MonacoValueAsync());
    }

    [Fact]
    public async Task A_path_that_can_not_be_used_says_why_and_uploads_nothing()
    {
        _ = Tree.File("docs/bad.md", "");
        string before = Tree.Snapshot();
        await OpenAsync("/docs/bad.md?edit");
        await WaitForEditorAsync();

        await SendFileAsync("#editor", "drop", "x.png", "image/png");
        foreach (string path in new[] { "./assets/", "../../x.png", "../vault/x.png" })
        {
            await PathField.FillAsync(path);
            await Page.Locator("#insert-media-confirm").ClickAsync();
            await Expect(Page.Locator("#insert-media-error")).ToBeVisibleAsync();
            await Expect(Modal).ToBeVisibleAsync();
        }

        Assert.Equal(before, Tree.Snapshot());
        Assert.Equal("", await MonacoValueAsync());
    }

    [Fact]
    public async Task Text_that_comes_with_a_picture_is_pasted_as_text()
    {
        _ = Tree.File("docs/cells.md", "");
        await OpenAsync("/docs/cells.md?edit");
        _ = await Page.EvaluateAsync("() => toggleSimpleView()");

        // As a spreadsheet's cells come: text and a picture of them
        bool handled = await Page.EvaluateAsync<bool>("""
            () => {
                const transfer = new DataTransfer();
                transfer.setData('text/plain', 'a\tb');
                transfer.items.add(new File(['bytes'], 'image.png', { type: 'image/png' }));
                const event = new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: transfer });
                document.getElementById('simple-editor').dispatchEvent(event);
                return event.defaultPrevented;
            }
            """);

        Assert.False(handled);
        await Expect(Modal).ToBeHiddenAsync();
    }

    [GeneratedRegex(@"^\./assets/\d{4}-\d\d-\d\d_\d\d-\d\d\.png$")]
    private static partial Regex TimeNamePattern();
}
