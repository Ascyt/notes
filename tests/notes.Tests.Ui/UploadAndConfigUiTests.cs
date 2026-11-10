using Microsoft.Playwright;

namespace notes.Tests.Ui;

/// <summary>The upload dialog (_Upload.cshtml), the toasts (_Toasts.cshtml) and the permissions editor (ConfigEditorView.cshtml).</summary>
public sealed class UploadAndConfigUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator Input => Page.Locator("#upload-input");

    private static FilePayload File(string name, byte[] contents) => new() { Name = name, MimeType = "application/octet-stream", Buffer = contents };

    [Fact]
    public async Task Uploading_files_through_the_dialog()
    {
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("one.md", "first"u8.ToArray()), File("two.md", "second"u8.ToArray())]);
        await Expect(Page.Locator("#upload-modal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#upload-list li")).ToHaveCountAsync(2);
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToBeVisibleAsync();
        await Expect(Page.Locator("a[href='/docs/one.md']")).ToBeVisibleAsync();
        Assert.Equal("first", Tree.Read("docs/one.md"));
        Assert.Equal("second", Tree.Read("docs/two.md"));
    }

    [Fact]
    public async Task A_file_larger_than_a_chunk_arrives_whole()
    {
        byte[] contents = new byte[9 * 1024 * 1024 + 123];
        Random.Shared.NextBytes(contents);
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("big.bin", contents)]);
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToBeVisibleAsync();
        Assert.Equal(contents, await System.IO.File.ReadAllBytesAsync(Tree.Physical("docs/big.bin"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_taken_name_gets_a_number()
    {
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("a.md", "new"u8.ToArray())]);
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToBeVisibleAsync();
        Assert.Equal("a", Tree.Read("docs/a.md"));
        Assert.Equal("new", Tree.Read("docs/a (1).md"));
    }

    [Fact]
    public async Task A_refused_upload_says_why_and_keeps_the_file_to_try_again()
    {
        // open.md is taken, and its numbered name may not be created
        Tree.Config("mixed", Cfg.Folder(Cfg.Perms(), ("open (1).md", Cfg.Perms(cdDeny: ["g:team"]))));
        await OpenAsync("/mixed");

        await Input.SetInputFilesAsync([File("open.md", "x"u8.ToArray())]);
        await Page.Locator("#upload-confirm").ClickAsync();

        // Names the file that failed
        await Expect(Page.Locator("#upload-error")).ToContainTextAsync("open.md");
        await Expect(Page.Locator("#upload-list li")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#upload-confirm")).ToBeEnabledAsync();
        Assert.False(Tree.Exists("mixed/open (1).md"));
    }

    [Fact]
    public async Task Removing_the_last_file_closes_the_dialog_and_cancel_uploads_nothing()
    {
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("x.md", "x"u8.ToArray())]);
        await Page.Locator("#upload-list button[title='Remove']").ClickAsync();
        await Expect(Page.Locator("#upload-modal")).ToBeHiddenAsync();

        await Input.SetInputFilesAsync([File("y.md", "y"u8.ToArray())]);
        await Page.Locator("#upload-cancel").ClickAsync();
        await Expect(Page.Locator("#upload-modal")).ToBeHiddenAsync();

        Assert.False(Tree.Exists("docs/x.md"));
        Assert.False(Tree.Exists("docs/y.md"));
    }

    [Fact]
    public async Task Where_nothing_may_be_added_there_is_no_upload_create_or_delete_at_all()
    {
        await OpenAsync("/shelf");

        await Expect(Page.Locator("#upload-button")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#delete-button")).ToHaveCountAsync(0);
        await Expect(Page.Locator("button[formaction='?create=file']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_file_that_may_be_deleted_but_not_renamed_says_why()
    {
        // Alice may delete b.md, but not add anything to nested (and so not give b.md another name there)
        Tree.Config("docs/nested", Cfg.Folder(Cfg.Perms(cdDeny: ["g:team"]), ("b.md", Cfg.Perms(cdAllow: ["g:team"]))));
        await OpenAsync("/docs/nested/b.md");

        await ExpectDisabledWithReason(Page.Locator("#rename-input"));
        await Expect(Page.Locator("#delete-button")).ToHaveAttributeAsync("aria-disabled", "false");
    }

    // ---- Toasts

    [Fact]
    public async Task Toasts_go_away_when_clicked()
    {
        await OpenAsync("/docs");
        _ = await Page.EvaluateAsync("() => notify('Hello <b>there</b>', 'info')");

        await Expect(Toast).ToHaveTextAsync("Hello <b>there</b>");
        await Toast.ClickAsync();

        await Expect(Page.Locator(".toast-card")).ToHaveCountAsync(0);
    }

    // ---- Permissions editor (admins)

    [Fact]
    public async Task The_permissions_editor_says_what_is_wrong_and_saves_what_is_right()
    {
        IBrowserContext admin = await NewContextAsync(Users.Admin);
        IPage page = await admin.NewPageAsync();
        _ = await page.GotoAsync("/docs?config");
        _ = await page.WaitForFunctionAsync("() => window.editor && typeof window.editor.getValue === 'function'");

        _ = await page.EvaluateAsync("() => window.editor.setValue('{ \"version\": \"1.0.0\", \"perms\": { \"inherit\": true, \"rwAllow\": [\"a\"] }, \"files\": [] }')");
        await page.Locator("#save-button").ClickAsync();
        await Assertions.Expect(page.Locator("#config-errors")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".toast-card[data-kind='danger']")).ToBeVisibleAsync();
        Assert.Null(Tree.ReadConfig("docs"));

        _ = await page.EvaluateAsync("() => window.editor.setValue('{ \"version\": \"1.0.0\", \"perms\": { \"inherit\": false, \"rwAllow\": [\"u:carol\"] }, \"files\": [] }')");
        await page.Locator("#save-button").ClickAsync();
        await Assertions.Expect(page.Locator("#config-errors")).ToBeHiddenAsync();
        await Eventually(() => Tree.ReadConfig("docs")?.Contains("u:carol") == true, "the permissions are saved");
        await ExpectDisabledWithReason(page.Locator("#save-button"));
        // What was saved has the entry for the file, and the editor shows it
        _ = await page.WaitForFunctionAsync("() => window.editor.getValue().includes('a.md')");

        await page.Locator("#reset-button").ClickAsync();
        Assert.Contains("\"inherit\": true", await page.EvaluateAsync<string>("() => window.editor.getValue()"));
        Assert.Contains("u:carol", Tree.ReadConfig("docs"));
        await admin.DisposeAsync();
    }

    [Fact]
    public async Task The_permissions_editor_zooms_like_the_editor_of_a_file()
    {
        const string MonacoFontSize = "() => window.editor.getOption(monaco.editor.EditorOption.fontSize)";
        IBrowserContext admin = await NewContextAsync(Users.Admin);
        IPage page = await admin.NewPageAsync();
        _ = await page.GotoAsync("/docs?config");
        _ = await page.WaitForFunctionAsync("() => window.editor && typeof window.editor.getValue === 'function'");
        double usualSize = await page.EvaluateAsync<double>(MonacoFontSize);

        await page.Locator("#zoom-out-button").ClickAsync();
        Assert.True(await page.EvaluateAsync<double>(MonacoFontSize) < usualSize);

        // The same zoom in the editor of a file
        _ = await page.GotoAsync("/docs/a.md?edit");
        _ = await page.WaitForFunctionAsync("() => window.editor && typeof window.editor.getValue === 'function'");
        Assert.True(await page.EvaluateAsync<double>(MonacoFontSize) < usualSize);
        await admin.DisposeAsync();
    }
}
