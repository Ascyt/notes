using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Notes.Features.Components.Selection;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;

namespace Notes.Tests.Ui;

/// <summary>
/// Downloading as an archive (_ArchiveModal: the dialog, from the toolbar and the selection card) and what covers the page while something
/// long runs (whileBusy, _BusyOverlay), here and for renaming.
/// </summary>
public sealed class ArchiveUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator Modal => Page.Locator("#archive-modal");
    private ILocator Overlay => Page.Locator("#busy-overlay");
    private ILocator StoreOption => Page.Locator("#archive-compression-store");
    private ILocator MinOption => Page.Locator("#archive-compression-min");

    private IReadOnlyList<string> ServerSelection
        => App.Services.GetRequiredService<ISelectionService>().Get(Users.Alice.Sub!).Paths;

    private async Task SelectAsync(string directory, params string[] paths)
    {
        await OpenAsync("/" + directory);
        foreach (string path in paths)
            await Page.Locator($".entry-row[data-path='{path}'] .entry-select").First.CheckAsync();
        await Eventually(() => paths.All(ServerSelection.Contains), "the server has the selection");
        await Expect(Page.Locator("#selection-count")).ToHaveAttributeAsync("data-count", paths.Length.ToString());
    }

    /// <summary>
    /// Clicks what starts the download of an archive and returns its name and bytes. The browser sends a download that a link starts without the
    /// header the tests sign in with (nor can it be routed), where a real session's cookie goes along: so the address the page downloads is
    /// asked for again with it.
    /// </summary>
    private async Task<(string Name, byte[] Bytes)> DownloadArchiveAsync(Func<Task> start)
    {
        IDownload download = await Page.RunAndWaitForDownloadAsync(start);
        Assert.Matches(@"\?archive=[0-9a-f-]{36}$", download.Url);
        IAPIResponse response = await Context.APIRequest.GetAsync(download.Url);
        Assert.True(response.Ok, $"{download.Url}: {response.Status}");
        string disposition = response.Headers["content-disposition"];
        string name = Regex.Match(disposition, @"filename=""?([^"";]+)").Groups[1].Value;
        return (name, await response.BodyAsync());
    }

    /// <summary>Clicks what starts the download of a file and returns the downloaded file's name and bytes.</summary>
    private async Task<(string Name, byte[] Bytes)> DownloadAsync(Func<Task> start)
    {
        IDownload download = await Page.RunAndWaitForDownloadAsync(start);
        string path = await download.PathAsync();
        return (download.SuggestedFilename, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    private static List<string> SevenZipNames(byte[] bytes)
    {
        using IArchive archive = SevenZipArchive.OpenArchive(new MemoryStream(bytes), new SharpCompress.Readers.ReaderOptions());
        return [.. archive.Entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Key!.Replace('\\', '/'))];
    }

    [Fact]
    public async Task The_toolbar_asks_how_and_the_lowest_level_is_the_formats()
    {
        await OpenAsync("/docs");
        await Page.Locator("#download-button").ClickAsync();

        await Expect(Modal).ToBeVisibleAsync();
        await Expect(Page.Locator("#archive-format-7z")).ToBeCheckedAsync();
        await Expect(StoreOption).ToBeDisabledAsync();
        await Expect(MinOption).ToBeEnabledAsync();
        await Page.Locator("#archive-compression").SelectOptionAsync("min");

        // A zip is stored without compression at its lowest level, and the choice goes along
        await Page.Locator("label[for='archive-format-zip']").ClickAsync();

        await Expect(StoreOption).ToBeEnabledAsync();
        await Expect(MinOption).ToBeDisabledAsync();
        await Expect(Page.Locator("#archive-compression")).ToHaveValueAsync("store");

        await Page.Locator("label[for='archive-format-7z']").ClickAsync();
        await Expect(Page.Locator("#archive-compression")).ToHaveValueAsync("min");
    }

    [Fact]
    public async Task An_encrypted_zip_is_downloaded_and_the_choice_but_not_the_password_is_kept()
    {
        await OpenAsync("/docs");
        await Page.Locator("#download-button").ClickAsync();
        await Page.Locator("label[for='archive-format-zip']").ClickAsync();
        await Page.Locator("#archive-compression").SelectOptionAsync("store");
        await Page.Locator("#archive-password").FillAsync("correct horse");

        (string name, byte[] bytes) = await DownloadArchiveAsync(() => Page.Locator("#archive-confirm").ClickAsync());

        Assert.EndsWith(".zip", name);
        using (ZipFile zip = new(new MemoryStream(bytes)) { Password = "correct horse" })
        {
            ZipEntry entry = zip.GetEntry("a.md");
            Assert.Equal(256, entry.AESKeySize);
            Assert.Equal(CompressionMethod.Stored, entry.CompressionMethod);
            using StreamReader reader = new(zip.GetInputStream(entry));
            Assert.Equal("a", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }

        await Expect(Overlay).ToBeHiddenAsync();
        await Page.Locator("#download-button").ClickAsync();
        await Expect(Page.Locator("#archive-format-zip")).ToBeCheckedAsync();
        await Expect(Page.Locator("#archive-compression")).ToHaveValueAsync("store");
        await Expect(Page.Locator("#archive-password")).ToHaveValueAsync("");
    }

    [Fact]
    public async Task Enter_in_the_password_field_downloads()
    {
        await OpenAsync("/docs");
        await Page.Keyboard.PressAsync("Alt+PageDown");
        await Expect(Modal).ToBeVisibleAsync();
        await Page.Locator("#archive-password").FillAsync("pw");

        (string name, byte[] bytes) = await DownloadArchiveAsync(() => Page.Locator("#archive-password").PressAsync("Enter"));

        Assert.EndsWith(".7z", name);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task One_selected_file_downloads_as_it_is_without_asking()
    {
        await SelectAsync("docs", "docs/a.md");

        (string name, byte[] bytes) = await DownloadAsync(() => Page.Locator("#selection-download").ClickAsync());

        Assert.Equal("a.md", name);
        Assert.Equal("a", System.Text.Encoding.UTF8.GetString(bytes));
        await Expect(Modal).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Several_selected_entries_download_as_an_archive_from_another_directory()
    {
        await SelectAsync("docs", "docs/a.md", "docs/nested");
        await OpenAsync("/team");

        await Page.Locator("#selection-download").ClickAsync();
        await Expect(Modal).ToBeVisibleAsync();
        (string name, byte[] bytes) = await DownloadArchiveAsync(() => Page.Locator("#archive-confirm").ClickAsync());

        Assert.EndsWith(".7z", name);
        Assert.Equal(["a.md", "nested/b.md"], SevenZipNames(bytes).Order(StringComparer.Ordinal));
        Assert.Equal(2, ServerSelection.Count);
    }

    [Fact]
    public async Task While_the_archive_is_made_the_page_is_covered_and_leaving_it_asks_first()
    {
        TaskCompletionSource answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync(new Regex(@"\?archive=directory"), async route =>
        {
            await answer.Task;
            await route.ContinueAsync();
        });
        await OpenAsync("/docs");
        await Page.Locator("#download-button").ClickAsync();
        await Page.Locator("#archive-confirm").ClickAsync();

        await Expect(Overlay).ToBeVisibleAsync();
        Assert.True(await Page.EvaluateAsync<bool>("window.isBusy()"));

        IDownload download = await Page.RunAndWaitForDownloadAsync(() =>
        {
            answer.SetResult();
            return Task.CompletedTask;
        });
        Assert.Matches(@"\?archive=[0-9a-f-]{36}$", download.Url);
        await Expect(Overlay).ToBeHiddenAsync();
        Assert.False(await Page.EvaluateAsync<bool>("window.isBusy()"));
    }

    [Fact]
    public async Task A_refusal_of_the_server_is_shown_and_the_page_is_usable_again()
    {
        await Page.RouteAsync(new Regex(@"\?archive=directory"), route => route.FulfillAsync(new() { Status = 409, Body = "refused" }));
        await OpenAsync("/docs");
        await Page.Locator("#download-button").ClickAsync();
        await Page.Locator("#archive-confirm").ClickAsync();

        await Expect(ToastOf("danger")).ToBeVisibleAsync();
        await Expect(Overlay).ToBeHiddenAsync();
        await Expect(Modal).ToBeHiddenAsync();
    }

    [Fact]
    public async Task While_an_entry_is_renamed_the_page_is_covered_and_leaving_it_asks_first()
    {
        TaskCompletionSource answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync(new Regex(@"\?rename="), async route =>
        {
            await answer.Task;
            await route.ContinueAsync();
        });
        await OpenAsync("/docs");
        await Page.Locator("#rename-input").FillAsync("renamed");
        await Page.Locator("#rename-input").PressAsync("Enter");

        await Expect(Overlay).ToBeVisibleAsync();
        Assert.True(await Page.EvaluateAsync<bool>("window.isBusy()"));
        // Shortcuts do nothing meanwhile (Home would go to the root)
        await Page.Keyboard.PressAsync("Home");
        Assert.EndsWith("/docs", new Uri(Page.Url).AbsolutePath);

        answer.SetResult();
        await Page.WaitForURLAsync(new Regex("/renamed$"));
        Assert.True(Tree.IsDirectory("renamed"));
    }
}
