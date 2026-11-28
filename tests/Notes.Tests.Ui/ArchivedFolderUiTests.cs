using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Ui;

/// <summary>An app whose requests don't wait for unzipping (see <see cref="NoWaitForUnzippingNotesAppFactory"/>).</summary>
public sealed class NoWaitForUnzippingFixture() : UiFixture(new NoWaitForUnzippingNotesAppFactory());

/// <summary>
/// Archived folders in the browser: going into a zipped one, and a request of the page (a save) that finds it zipped again. Each test has a
/// folder of its own, as the app keeps which ones are unzipped across the tests.
/// </summary>
public sealed class ArchivedFolderUiTests(NoWaitForUnzippingFixture fixture) : UiTest(fixture), IClassFixture<NoWaitForUnzippingFixture>
{
    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();

    /// <summary>docs/&lt;name&gt; with note.md, archived and zipped.</summary>
    private async Task<string> ZippedFolderAsync(string name)
    {
        Tree.File($"docs/{name}/note.md", "inside");
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        config["archive"] = new JsonObject { ["enabled"] = true };
        Tree.Config($"docs/{name}", config.ToJsonString());
        string folder = Tree.Physical($"docs/{name}");
        await Archives.ConvertAsync(folder);
        Assert.True(await Archives.ZipAsync(folder));
        return folder;
    }

    [Fact]
    public async Task Going_into_a_zipped_folder_shows_how_far_unzipping_got_then_the_folder()
    {
        string folder = await ZippedFolderAsync("vault-open");

        await OpenAsync("/docs/vault-open");

        // The page of the unzipping (it never waits here) loads the folder itself once it is unzipped
        await Expect(Page.Locator(".entry-row[data-path='docs/vault-open/note.md']")).ToBeVisibleAsync();
        await Expect(Page.Locator(".sticky-breadcrumb [data-archived-mark]")).ToHaveCountAsync(1);
        Assert.NotNull(Archives.MountedAt(folder));
    }

    [Fact]
    public async Task An_archived_folder_has_the_zipper_in_the_listing()
    {
        _ = await ZippedFolderAsync("vault-listed");

        await OpenAsync("/docs");

        await Expect(Page.Locator(".entry-row[data-path='docs/vault-listed'] [data-archived-mark]")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task The_zipper_says_whether_it_is_zipped_or_unzipped_now()
    {
        string folder = await ZippedFolderAsync("vault-marks");

        await OpenAsync("/docs");
        await Expect(Page.Locator(".entry-row[data-path='docs/vault-marks'] [data-archived-mark='zipped']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("text-primary"));

        _ = await Archives.Unzip(folder).Finished;
        await OpenAsync("/docs");
        await Expect(Page.Locator(".entry-row[data-path='docs/vault-marks'] [data-archived-mark='unzipped']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("fa-regular"));
    }

    [Fact]
    public async Task The_archive_button_zips_the_folder_the_page_is_in_and_goes_out_of_it()
    {
        string folder = await ZippedFolderAsync("vault-button");
        _ = await Archives.Unzip(folder).Finished;

        await OpenAsync("/docs/vault-button");
        await Expect(Page.Locator("#zip-button")).ToBeVisibleAsync();
        await Expect(Page.Locator("#lock-button")).ToBeHiddenAsync();
        await Page.Keyboard.PressAsync("Alt+l");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/docs$"));
        await Eventually(() => Archives.IsZipped(folder) && Archives.ZipProgressOf(folder) is null, "it is zipped");
        // (No toast: the page it goes to shows the folder being zipped)
        await Expect(ToastOf("info")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task An_admin_downloads_it_as_it_is_kept_unless_ticked_and_the_options_are_greyed_out_meanwhile()
    {
        string folder = await ZippedFolderAsync("vault-as-is");
        _ = await Archives.Unzip(folder).Finished;
        IBrowserContext adminContext = await NewContextAsync(Users.Admin);
        IPage admin = await adminContext.NewPageAsync();
        await admin.GotoAsync("/docs/vault-as-is");

        // The folder itself, not ticked at first: the download is its archive, so the options don't apply
        await admin.Locator("#download-button").ClickAsync();
        ILocator box = admin.Locator(".archive-folder-unzip[data-path='docs/vault-as-is']");
        await Expect(box).Not.ToBeCheckedAsync();
        await Expect(admin.Locator("#archive-compression")).ToBeDisabledAsync();
        await Expect(admin.Locator("#archive-whole-as-is")).ToBeVisibleAsync();
        // Ticked (here through the box above them all), it is unzipped for the download like any other folder
        await admin.Locator("#archive-unzip-all").CheckAsync();
        await Expect(box).ToBeCheckedAsync();
        await Expect(admin.Locator("#archive-compression")).ToBeEnabledAsync();
        await admin.Locator("#archive-unzip-all").UncheckAsync();

        // (The test's sign-in header doesn't go with a download a link starts, see ArchiveUiTests: the request that makes it is enough here)
        IResponse made = await admin.RunAndWaitForResponseAsync(() => admin.Locator("#archive-confirm").ClickAsync(),
            response => response.Url.Contains("?archive=as-is"));
        Assert.Equal(202, made.Status);
        await Eventually(() => Archives.IsZipped(folder) && Archives.ZipProgressOf(folder) is null, "it is zipped for it");
        await adminContext.DisposeAsync();

        // Not for others: no boxes, but they are told it is unzipped for it
        await OpenAsync("/docs/vault-as-is");
        await Page.Locator("#download-button").ClickAsync();
        await Expect(Page.Locator("#archive-compression")).ToBeEnabledAsync();
        await Expect(Page.Locator(".archive-folder-unzip")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#archive-folders-user")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_toolbar_zips_the_folder_of_the_page()
    {
        string folder = await ZippedFolderAsync("vault-toolbar");
        _ = await Archives.Unzip(folder).Finished;

        await OpenAsync("/docs/vault-toolbar");
        await Page.Locator("#zip-entry-button").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/docs$"));
        await Eventually(() => Archives.IsZipped(folder) && Archives.ZipProgressOf(folder) is null, "it is zipped");
    }

    [Fact]
    public async Task A_folder_being_zipped_has_a_bar_in_the_listing_that_turns_into_the_zipper_once_it_is()
    {
        string folder = await ZippedFolderAsync("vault-zipping");
        _ = await Archives.Unzip(folder).Finished;
        FolderArchiver archiver = App.Services.GetRequiredService<FolderArchiver>();
        using ManualResetEventSlim reached = new();
        using ManualResetEventSlim release = new();
        // Held once the new archive is written (the app is shared by the tests of the class: let go of in any case)
        archiver.StepReached = step =>
        {
            if (step != ArchiveStep.ArchiveWritten)
                return;
            reached.Set();
            _ = release.Wait(TimeSpan.FromSeconds(30));
        };
        try
        {
            Task<bool> zipping = Task.Run(() => Archives.ZipAsync(folder, stopsWhenOpened: true));
            Assert.True(reached.Wait(TimeSpan.FromSeconds(30)));

            await OpenAsync("/docs");
            ILocator row = Page.Locator(".entry-row[data-path='docs/vault-zipping']");
            await Expect(row.Locator("[data-archived-mark='zipping'] .progress")).ToBeVisibleAsync();
            // ...with what it is doing next to it, once the page asked (held right after writing)
            await Expect(row.Locator(".archived-zipping-detail")).ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"^Writing the archive: \d+ of \d+ files, .* \(70%\)$"));

            release.Set();
            Assert.True(await zipping);
            await Expect(row.Locator("[data-archived-mark='zipped']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("text-primary"));
            await Expect(row.Locator("[data-archived-mark='zipping']")).ToHaveCountAsync(0);
        }
        finally
        {
            release.Set();
            archiver.StepReached = null;
        }
    }

    [Fact]
    public async Task A_save_into_a_folder_that_was_zipped_meanwhile_unzips_it_and_is_saved()
    {
        string folder = await ZippedFolderAsync("vault-save");
        await OpenAsync("/docs/vault-save/note.md?edit");
        await WaitForEditorAsync();
        await Expect(Page.Locator("#save-button")).ToHaveAttributeAsync("aria-disabled", "true");

        // Zipped behind the page's back (as after a while without use)
        Assert.True(await Archives.ZipAsync(folder));
        await Page.Keyboard.PressAsync("Control+End");
        await Page.Keyboard.TypeAsync(" and more");
        await Page.Keyboard.PressAsync("Control+s");

        await Eventually(() => Archives.MountedAt(folder) is { } mounted
            && File.ReadAllText(Path.Combine(mounted.PhysicalPath, "note.md")) == "inside and more", "the file is saved in the unzipped folder");
        await Expect(Page.Locator("#save-button")).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(Page.Locator("#busy-overlay")).ToBeHiddenAsync();
    }
}
