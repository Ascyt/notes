using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using notes.Features.History;
using notes.Features.HistoryViewing.Requests;

namespace notes.Tests.Ui;

/// <summary>
/// The scripts of the history: the dialog behind the history button (_HistoryModal), with the address it makes and the times it refuses, and
/// the card of an earlier state (_EarlierStateCard), which keeps the time on the links of the page.
/// </summary>
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

    /// <summary>Commits the notes as they are, and moves the app's clock past it. Returns a time (as the address has it) of which this is the state before.</summary>
    private string CommitState()
    {
        App.Services.GetRequiredService<HistoryService>().CommitPending(CommitReason.Idle);
        DateTimeOffset during = App.Time.GetUtcNow().AddSeconds(30);
        App.Time.Advance(TimeSpan.FromMinutes(1));
        return EarlierStateTime.Format(during);
    }

    // ---- The dialog

    [Fact]
    public async Task View_opens_the_same_entry_before_the_chosen_time_in_utc_without_the_rest_of_the_query()
    {
        await OpenAsync("/docs/a.md?edit");
        await Page.Locator("#history-button").ClickAsync();
        await Expect(Modal).ToBeVisibleAsync();
        await Expect(Field).ToBeFocusedAsync();

        await Field.FillAsync("2026-01-02T03:04");
        string expected = await InUtcAsync("2026-01-02T03:04");
        // (There is no state then, so the server sends it on: the address asked for is what counts)
        IRequest request = await Page.RunAndWaitForRequestAsync(() => ViewButton.ClickAsync(), request => request.IsNavigationRequest);

        Assert.EndsWith("/docs/a.md?before=" + expected, request.Url);
    }

    [Fact]
    public async Task Enter_in_the_field_views_and_the_shortcut_opens_the_dialog()
    {
        await OpenAsync("/docs");
        await Page.Keyboard.PressAsync("Alt+H");
        await Expect(Modal).ToBeVisibleAsync();

        await Field.FillAsync("2025-12-31T23:59");
        string expected = await InUtcAsync("2025-12-31T23:59");
        IRequest request = await Page.RunAndWaitForRequestAsync(() => Field.PressAsync("Enter"), request => request.IsNavigationRequest);

        Assert.EndsWith("/docs?before=" + expected, request.Url);
    }

    [Fact]
    public async Task The_field_starts_at_the_time_that_is_shown()
    {
        string before = CommitState();
        // The browser goes by the app's clock, which the tests move, to tell what lies in the future
        await Page.Clock.InstallAsync(new ClockInstallOptions { TimeDate = App.Time.GetUtcNow().UtcDateTime });
        await OpenAsync("/docs?before=" + before);
        await Page.Locator("#history-button").ClickAsync();

        string expected = await Page.EvaluateAsync<string>("""
            value => { const d = new Date(value), p = n => String(n).padStart(2, '0');
                       return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + 'T' + p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds()); }
            """, before);
        await Expect(Field).ToHaveValueAsync(expected);
        // ...which can be seen as it was, so it can be viewed again
        await Expect(Page.Locator("#history-times li[data-after]")).ToHaveCountAsync(1);
        await Expect(ViewButton).ToHaveAttributeAsync("aria-disabled", "false");
    }

    [Fact]
    public async Task An_empty_or_future_time_is_refused_with_a_reason()
    {
        await OpenAsync("/docs");
        await Page.Locator("#history-button").ClickAsync();
        // There is no history yet, so there are no times to go by: only the server decides then
        await Expect(Page.Locator("#history-times li")).Not.ToHaveTextAsync(new Regex("Loading"));
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
    public async Task The_dialog_lists_the_times_and_refuses_any_other()
    {
        _ = CommitState();
        await OpenAsync("/docs");
        await Page.Locator("#history-button").ClickAsync();

        // One time: from the first state on (the empty one the history begins with has no docs)
        await Expect(Page.Locator("#history-times li[data-after]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#history-times li[data-after]")).ToHaveAttributeAsync("data-until", "");

        await Field.FillAsync("2000-01-01T00:00");
        await ExpectDisabledWithReason(ViewButton);
        await Field.PressAsync("Enter");
        Assert.EndsWith("/docs", Page.Url);
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

    // ---- An earlier state

    [Fact]
    public async Task Every_link_to_the_notes_keeps_the_time_and_the_card_leads_back_to_now()
    {
        string before = CommitState();
        await OpenAsync("/docs?before=" + before);

        await Expect(Page.Locator("#history-card")).ToBeVisibleAsync();
        await Expect(Page.Locator("a[href^='/docs/a.md']")).ToHaveAttributeAsync("href", "/docs/a.md?before=" + before);
        await Expect(Page.Locator("a.navbar-brand")).ToHaveAttributeAsync("href", "/?before=" + before);
        await Expect(Page.Locator("#download-button")).ToHaveAttributeAsync("href", "/docs?download&before=" + before);
        // Not the app's own pages, nor other sites
        await Expect(Page.Locator("a[href^='/_/']").First).Not.ToHaveAttributeAsync("href", new Regex("before="));
        await Expect(Page.Locator("a[href^='https://github.com']").First).Not.ToHaveAttributeAsync("href", new Regex("before="));

        // Following a link stays at that time
        await Page.Locator("a[href^='/docs/a.md']").ClickAsync();
        await Page.WaitForURLAsync(new Regex(Regex.Escape("/docs/a.md?before=" + before) + "$"));
        await Expect(Page.Locator("#history-card")).ToBeVisibleAsync();

        await Page.Locator("#history-exit").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/docs/a\\.md$"));
        await Expect(Page.Locator("#history-card")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Escape_leads_back_to_now_unless_a_dialog_is_open()
    {
        string before = CommitState();
        await OpenAsync("/docs?before=" + before);

        // An open dialog takes it first
        await Page.Locator("#history-button").ClickAsync();
        await Expect(Field).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Modal).ToBeHiddenAsync();
        Assert.Contains("before=", Page.Url);

        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForURLAsync(new Regex("/docs$"));
    }

    [Fact]
    public async Task An_address_that_can_not_be_shown_goes_to_now_and_says_why()
    {
        _ = CommitState();

        await OpenAsync("/docs/a.md?before=2000-01-01T00:00:00Z");

        Assert.EndsWith("/docs/a.md", Page.Url);
        await Expect(ToastOf("danger")).ToBeVisibleAsync();
        // Once only
        await Page.ReloadAsync();
        await Expect(Page.Locator(".toast-card")).ToHaveCountAsync(0);
    }
}
