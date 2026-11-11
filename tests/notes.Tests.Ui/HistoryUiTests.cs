using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace notes.Tests.Ui;

/// <summary>The dialog behind the history button (_HistoryModal): the address it makes, and the times it refuses.</summary>
public sealed class HistoryUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    // Admins have the history everywhere
    protected override TestUser User => Users.Admin;

    private ILocator Modal => Page.Locator("#history-modal");
    private ILocator Field => Page.Locator("#history-before");
    private ILocator ViewButton => Page.Locator("#history-apply");

    /// <summary>What the browser makes of a local time of the field: the same time in UTC, as the address has it.</summary>
    private async Task<string> InUtcAsync(string localTime)
        => await Page.EvaluateAsync<string>("value => new Date(value).toISOString().replace(/\\.\\d{3}Z$/, 'Z')", localTime);

    [Fact]
    public async Task View_opens_the_same_entry_before_the_chosen_time_in_utc_without_the_rest_of_the_query()
    {
        await OpenAsync("/docs/a.md?edit");
        await Page.Locator("#history-button").ClickAsync();
        await Expect(Modal).ToBeVisibleAsync();
        await Expect(Field).ToBeFocusedAsync();

        await Field.FillAsync("2026-01-02T03:04");
        string expected = await InUtcAsync("2026-01-02T03:04");
        await ViewButton.ClickAsync();

        await Page.WaitForURLAsync(new Regex(Regex.Escape("/docs/a.md?before=" + expected) + "$"));
    }

    [Fact]
    public async Task Enter_in_the_field_views_and_the_shortcut_opens_the_dialog()
    {
        await OpenAsync("/docs");
        await Page.Keyboard.PressAsync("Alt+Shift+H");
        await Expect(Modal).ToBeVisibleAsync();

        await Field.FillAsync("2025-12-31T23:59");
        string expected = await InUtcAsync("2025-12-31T23:59");
        await Field.PressAsync("Enter");

        await Page.WaitForURLAsync(new Regex(Regex.Escape("/docs?before=" + expected) + "$"));
    }

    [Fact]
    public async Task The_field_starts_at_the_time_that_is_shown()
    {
        await OpenAsync("/docs?before=2026-03-04T05:06:00Z");
        await Page.Locator("#history-button").ClickAsync();

        string expected = await Page.EvaluateAsync<string>("""
            () => { const d = new Date('2026-03-04T05:06:00Z'), p = n => String(n).padStart(2, '0');
                    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + 'T' + p(d.getHours()) + ':' + p(d.getMinutes()); }
            """);
        await Expect(Field).ToHaveValueAsync(expected);
    }

    [Fact]
    public async Task An_empty_or_future_time_is_refused_with_a_reason()
    {
        await OpenAsync("/docs");
        await Page.Locator("#history-button").ClickAsync();
        // Starts at now, which can be viewed
        await Expect(ViewButton).ToHaveAttributeAsync("aria-disabled", "false");

        await Field.FillAsync("2999-01-01T00:00");
        await ExpectDisabledWithReason(ViewButton);
        // (Also visible without hovering, on phones)
        await Expect(Page.Locator("#history-problem")).ToBeVisibleAsync();
        await Field.PressAsync("Enter");
        await ViewButton.ClickAsync(new LocatorClickOptions { Force = true });

        await Field.FillAsync("");
        await ExpectDisabledWithReason(ViewButton);
        await Field.PressAsync("Enter");

        Assert.EndsWith("/docs", Page.Url);
        await Expect(Modal).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Escape_closes_the_dialog()
    {
        await OpenAsync("/docs/a.md");
        await Page.Locator("#history-button").ClickAsync();
        // Focused once it is shown: Bootstrap doesn't close a dialog while it is still coming in
        await Expect(Field).ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Modal).ToBeHiddenAsync();
        Assert.EndsWith("/docs/a.md", Page.Url);
    }
}
