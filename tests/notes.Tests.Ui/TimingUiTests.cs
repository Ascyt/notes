using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using notes.Features.Components.Selection;
using notes.Features.Helpers;

namespace notes.Tests.Ui;

/// <summary>
/// What the pages do when time runs out or something runs long: a selection the server forgot, and a transfer running in another tab. Each
/// test has an app of its own, as the clock only moves forward. Skip them with <c>--filter-not-trait "Category=Timing"</c>.
/// </summary>
[Trait("Category", "Timing")]
public sealed class TimingUiTests(TimingUiTests.Fixture fixture) : UiTest(fixture), IClassFixture<TimingUiTests.Fixture>
{
    /// <summary>An app of its own, so that moving the clock affects no other test class.</summary>
    public sealed class Fixture : UiFixture;

    private ILocator Bar => Page.Locator("#selection-bar");

    private IReadOnlyList<string> ServerSelection => App.Services.GetRequiredService<ISelectionService>().Get(Users.Alice.Sub!).Paths;

    private async Task SelectAsync(string directory, string path)
    {
        await OpenAsync("/" + directory);
        await Page.Locator($"tr.entry-row[data-path='{path}'] .entry-select").CheckAsync();
        await Eventually(() => ServerSelection.Contains(path), "the server has the selection");
    }

    /// <summary>
    /// The page only takes an answer about the selection that is newer than what it shows. A selection that was forgotten (or one from before
    /// the server restarted) must therefore look older than the new one, or the page would show the old selection until it is reloaded.
    /// </summary>
    [Fact]
    public async Task A_page_whose_selection_the_server_forgot_says_so_and_shows_the_selection_as_it_is()
    {
        await SelectAsync("docs", "docs/a.md");
        await OpenAsync("/team");
        await Expect(Bar).ToBeVisibleAsync();

        App.Time.Advance(TimeSpan.FromMinutes(31));
        await Page.Locator("#selection-copy").ClickAsync();

        await Expect(ToastOf("danger")).ToBeVisibleAsync();
        await Expect(Bar).ToBeHiddenAsync();
        Assert.False(Tree.Exists("team/a.md"));
    }

    [Fact]
    public async Task Another_tab_waits_for_a_running_transfer_and_then_shows_the_result()
    {
        await SelectAsync("docs", "docs/a.md");
        await OpenAsync("/team");
        IPage other = await Context.NewPageAsync();

        Task copied;
        using (await App.Services.GetRequiredService<StructureLock>().EnterAsync())
        {
            await Page.Locator("#selection-copy").ClickAsync();
            await Expect(Page.Locator("#selection-spinner")).ToBeVisibleAsync();
            await Eventually(() => App.Services.GetRequiredService<ISelectionService>().Get(Users.Alice.Sub!).Busy, "the transfer has started");

            // The other tab sees that the selection is busy: no actions, a spinner, the boxes disabled
            _ = await other.GotoAsync("/docs");
            await Assertions.Expect(other.Locator("#selection-spinner")).ToBeVisibleAsync();
            await Assertions.Expect(other.Locator("#selection-actions")).ToBeHiddenAsync();
            await Assertions.Expect(other.Locator("tr.entry-row[data-path='docs/a.md'] .entry-select")).ToBeDisabledAsync();
            copied = Expect(ToastOf("success")).ToBeVisibleAsync();
        }

        await copied;
        // ...and when it is done, it reloads (the selection is gone) by itself
        await Assertions.Expect(other.Locator("#selection-bar")).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(other.Locator("tr.entry-row[data-path='docs/a.md'] .entry-select")).ToBeEnabledAsync();
        Assert.True(Tree.Exists("team/a.md"));
    }
}
