using Microsoft.Playwright;

namespace Notes.Tests.Ui;

/// <summary>The upload dialog (_Upload.cshtml), the toasts (_Toasts.cshtml) and the configuration editor (ConfigEditorView.cshtml).</summary>
public sealed class UploadAndConfigUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator Input => Page.Locator("#upload-input");

    private static FilePayload File(string name, byte[] contents) => new() { Name = name, MimeType = "application/octet-stream", Buffer = contents };

    /// <summary>A zip of one file, encrypted with the password if there is one (as the app writes it).</summary>
    private static async Task<byte[]> ZipOf(string name, string contents, string? password)
    {
        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            Notes.Features.Components.Download.ZipFolderEntry root = new("", () =>
                [new Notes.Features.Components.Download.ZipFileEntry(name, DateTimeOffset.UtcNow,
                    () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contents)))]);
            await Notes.Features.Components.Archive.ArchiveWriter.WriteAsync(root, new Notes.Features.Components.Archive.ArchiveOptions(
                Notes.Features.Components.Archive.ArchiveFormat.Zip, Notes.Features.Components.Archive.ArchiveCompression.Store, password),
                target, CancellationToken.None);
            return await System.IO.File.ReadAllBytesAsync(target);
        }
        finally
        {
            System.IO.File.Delete(target);
        }
    }

    [Fact]
    public async Task Ticked_archives_are_unzipped_after_the_upload_and_one_that_cant_be_is_marked()
    {
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("plain-ui.zip", await ZipOf("a.md", "A", null)), File("secret-ui.zip", await ZipOf("b.md", "B", "pw"))]);
        await Expect(Page.Locator("#upload-unzip-all")).ToBeCheckedAsync();
        await Expect(Page.Locator(".upload-unzip-one")).ToHaveCountAsync(2);
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToBeVisibleAsync();
        await Eventually(() => Tree.Exists("docs/plain-ui/a.md"), "the plain one is unzipped");
        Assert.Equal("A", Tree.Read("docs/plain-ui/a.md"));
        // The encrypted one stays a file, with a red mark that says why
        await Eventually(() => Tree.ReadConfig("docs")?.Contains("unzipError") == true, "the encrypted one failed");
        _ = await Page.ReloadAsync();
        await Expect(Page.Locator("[data-unzip-failed]")).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("encrypted"));
        Assert.True(Tree.IsFile("docs/secret-ui.zip"));
    }

    [Fact]
    public async Task An_unticked_archive_is_uploaded_as_a_file_and_the_box_above_shows_a_dash()
    {
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("as-file-ui.zip", await ZipOf("a.md", "A", null)), File("other-ui.zip", await ZipOf("b.md", "B", null))]);
        await Page.Locator(".upload-unzip-one").First.UncheckAsync();
        Assert.True(await Page.Locator("#upload-unzip-all").EvaluateAsync<bool>("box => box.indeterminate"));
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToBeVisibleAsync();
        await Eventually(() => Tree.Exists("docs/other-ui/b.md"), "the ticked one is unzipped");
        Assert.True(Tree.IsFile("docs/as-file-ui.zip"));
        Assert.False(Tree.Exists("docs/as-file-ui"));
    }

    [Fact]
    public async Task The_box_above_unticks_all_archives()
    {
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("none-ui.zip", await ZipOf("a.md", "A", null))]);
        await Page.Locator("#upload-unzip-all").UncheckAsync();
        await Expect(Page.Locator(".upload-unzip-one")).Not.ToBeCheckedAsync();
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToBeVisibleAsync();
        Assert.True(Tree.IsFile("docs/none-ui.zip"));
        Assert.DoesNotContain("unzipPending", Tree.ReadConfig("docs") ?? "");
    }

    [Fact]
    public async Task A_file_that_is_there_already_the_same_is_not_uploaded_again()
    {
        await OpenAsync("/docs");

        // a.md is "a"; b-ui.md is new
        await Input.SetInputFilesAsync([File("a.md", "a"u8.ToArray()), File("b-ui.md", "b"u8.ToArray())]);
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToContainTextAsync("there already");
        Assert.False(Tree.Exists("docs/a (1).md"));
        Assert.Equal("b", Tree.Read("docs/b-ui.md"));
    }

    [Fact]
    public async Task Cancel_stops_the_upload_and_keeps_nothing_of_the_file_being_sent()
    {
        await OpenAsync("/docs");
        // Each part takes a while, so that there is time to cancel
        await Page.RouteAsync("**/docs?upload=*", async route =>
        {
            await Task.Delay(1500);
            await route.ContinueAsync();
        });

        await Input.SetInputFilesAsync([File("slow-ui.bin", new byte[100])]);
        await Page.Locator("#upload-confirm").ClickAsync();
        await Expect(Page.Locator("#upload-progress")).ToBeVisibleAsync();
        await Expect(Page.Locator("#upload-cancel")).ToBeEnabledAsync();
        await Page.Locator("#upload-cancel").ClickAsync();

        await Expect(Page.Locator("#upload-modal")).ToBeHiddenAsync();
        await Expect(ToastOf("info")).ToContainTextAsync("cancelled");
        Assert.False(Tree.Exists("docs/slow-ui.bin"));
        Assert.Null(await Page.EvaluateAsync<string?>("() => localStorage.getItem('notes.upload')"));
    }

    [Fact]
    public async Task An_upload_that_didnt_finish_is_offered_on_the_next_page_and_continued_with_the_files_picked_again()
    {
        await OpenAsync("/docs");
        // As a closed tab leaves it: one file done, one not started
        _ = await Page.EvaluateAsync("""
            () => localStorage.setItem('notes.upload', JSON.stringify({
                directory: '/docs', beat: 0,
                files: [
                    { name: 'done-ui.md', size: 1, lastModified: 1, unzip: false, state: 'done', id: null, offset: 0 },
                    { name: 'later-ui.md', size: 5, lastModified: 1000, unzip: false, state: 'todo', id: null, offset: 0 }
                ]
            }))
            """);

        _ = await Page.GotoAsync("/shelf");
        await Expect(Page.Locator("#upload-resume-modal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#upload-resume-text")).ToContainTextAsync("1 of 2");
        await Page.Locator("#upload-resume-continue").ClickAsync();

        await Page.WaitForURLAsync("**/docs");
        await Expect(Page.Locator("#upload-modal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#upload-resume-note")).ToBeVisibleAsync();
        await Expect(Page.Locator("#upload-confirm")).ToBeDisabledAsync();
        await Input.SetInputFilesAsync([new FilePayload { Name = "later-ui.md", MimeType = "text/markdown", Buffer = "later"u8.ToArray() }]);
        // Matched to the one that was in it: the list stays two long
        await Expect(Page.Locator("#upload-list li")).ToHaveCountAsync(2);
        await Page.Locator("#upload-confirm").ClickAsync();

        await Expect(ToastOf("success")).ToBeVisibleAsync();
        Assert.Equal("later", Tree.Read("docs/later-ui.md"));
        Assert.Null(await Page.EvaluateAsync<string?>("() => localStorage.getItem('notes.upload')"));
    }

    [Fact]
    public async Task The_switch_is_only_there_for_archives()
    {
        await OpenAsync("/docs");

        await Input.SetInputFilesAsync([File("text-ui.md", "x"u8.ToArray())]);

        await Expect(Page.Locator("#upload-unzip-field")).ToBeHiddenAsync();
        await Expect(Page.Locator(".upload-unzip-one")).ToHaveCountAsync(0);
    }

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
    public async Task Where_nothing_may_be_added_there_is_no_upload_or_create_at_all()
    {
        await OpenAsync("/shelf");

        await Expect(Page.Locator("#upload-button")).ToHaveCountAsync(0);
        await Expect(Page.Locator("button[formaction='?create=file']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_file_that_may_be_deleted_but_not_renamed_says_why()
    {
        // Alice may delete b.md, but not add anything to nested (and so not give b.md another name there)
        Tree.Config("docs/nested", Cfg.Folder(Cfg.Perms(cdDeny: ["g:team"]), ("b.md", Cfg.Perms(cdAllow: ["g:team"]))));
        await OpenAsync("/docs/nested/b.md");

        await ExpectDisabledWithReason(Page.Locator("#rename-input"));
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

    // ---- Configuration editor (admins)

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
