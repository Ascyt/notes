using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Ui;

/// <summary>
/// The button of the toolbar for the storage operations that run, its dialog (_Operations), and the hourglass of what is pending. Each test has
/// a folder of its own, as the app keeps which ones are unzipped across the tests.
/// </summary>
public sealed class OperationsUiTests(NoWaitForUnzippingFixture fixture) : UiTest(fixture), IClassFixture<NoWaitForUnzippingFixture>
{
    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();
    private ILocator Button => Page.Locator("#operations-button");

    /// <summary>docs/&lt;name&gt; with note.md, archived (and unzipped).</summary>
    private async Task<string> ArchivedFolderAsync(string name)
    {
        Tree.File($"docs/{name}/note.md", "inside");
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        config["archive"] = new JsonObject { ["enabled"] = true };
        Tree.Config($"docs/{name}", config.ToJsonString());
        string folder = Tree.Physical($"docs/{name}");
        await Archives.ConvertAsync(folder);
        return folder;
    }

    [Fact]
    public async Task A_running_zipping_shows_the_button_and_its_row_in_the_dialog_where_it_can_be_cancelled()
    {
        string folder = await ArchivedFolderAsync("ops-ui");
        FolderArchiver archiver = App.Services.GetRequiredService<FolderArchiver>();
        using ManualResetEventSlim reached = new();
        using ManualResetEventSlim release = new();
        archiver.StepReached = step =>
        {
            if (step != ArchiveStep.ArchiveWritten)
                return;
            reached.Set();
            _ = release.Wait(TimeSpan.FromSeconds(30));
        };
        try
        {
            await OpenAsync("/docs");
            await Expect(Button).ToBeHiddenAsync();
            // Started by alice, from the page (as the zip button does)
            _ = await Page.EvaluateAsync("""
                () => fetch('/docs/ops-ui?zip=entry', { method: 'POST',
                    headers: { 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value } })
                """);
            Assert.True(reached.Wait(TimeSpan.FromSeconds(30)));

            // (The page asks every few seconds)
            await Expect(Button).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(Button).ToHaveClassAsync(new Regex("btn-outline-secondary"));
            await Button.ClickAsync();
            ILocator row = Page.Locator("#operations-list li");
            await Expect(row).ToHaveCountAsync(1);
            await Expect(row.Locator(".operation-title")).ToHaveTextAsync("Zipping an archived directory");
            await Expect(row.Locator(".operation-directory")).ToHaveAttributeAsync("href", "/docs/ops-ui");
            await Expect(row.Locator(".operation-user")).ToContainTextAsync("alice");
            await Expect(row.Locator(".operation-detail")).ToContainTextAsync("Writing the archive");
            await Expect(row.Locator(".progress")).ToBeVisibleAsync();

            await row.Locator(".operation-cancel").ClickAsync();
            // (Let go of once the server was told: otherwise it may simply finish first)
            await Eventually(() => App.Services.GetRequiredService<Notes.Features.Operations.StorageOperations>().Running.All(running => running.IsCancelled),
                "it was told to stop");
            release.Set();

            await Expect(row).ToHaveCountAsync(0);
            await Expect(Page.Locator("#operations-nothing")).ToBeVisibleAsync();
            Assert.NotNull(Archives.MountedAt(folder));
        }
        finally
        {
            release.Set();
            archiver.StepReached = null;
        }
    }

    [Fact]
    public async Task A_zipping_that_waits_has_an_hourglass_in_place_of_its_bar()
    {
        string folder = await ArchivedFolderAsync("ops-pending-ui");
        // Something uses it: the zipping waits for that (a while at most)
        IDisposable use = await Archives.UseAsync(folder);
        Task<bool> zipping = Task.Run(() => Archives.ZipAsync(folder));
        try
        {
            await Eventually(() => Archives.ZipProgressOf(folder) is not null, "it waits to be zipped");
            await OpenAsync("/docs");

            ILocator mark = Page.Locator(".entry-row[data-path='docs/ops-pending-ui'] [data-archived-mark='zipping']");
            await Expect(mark.Locator(".pending-mark")).ToBeVisibleAsync();
            await Expect(mark.Locator(".pending-mark")).ToHaveAttributeAsync("title", new Regex("^Pending"));
            await Expect(mark.Locator(".progress")).ToBeHiddenAsync();

            await Button.ClickAsync();
            await Expect(Page.Locator("#operations-list li .pending-mark")).ToBeVisibleAsync();
        }
        finally
        {
            // (Waited for in any case: one still running would be in the way of the next test)
            use.Dispose();
            _ = await zipping;
        }
    }

    [Fact]
    public async Task Who_isnt_logged_in_has_no_button()
    {
        IBrowserContext anonymous = await NewContextAsync(Users.Anonymous);
        IPage page = await anonymous.NewPageAsync();
        _ = await page.GotoAsync("/");

        await Assertions.Expect(page.Locator("#operations-button")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#operations-modal")).ToHaveCountAsync(0);
        await anonymous.DisposeAsync();
    }
}
