using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using notes.Features.History;
using notes.Features.HistoryViewing.Commits;

namespace notes.Tests.Ui;

/// <summary>
/// The script of the list of changes (_CommitSidebar): showing, focusing and hiding it, the view of a commit where it is always shown, going
/// through the commits and pages with the arrow keys (which are shortcuts of where they lead), and adding newer ones.
/// </summary>
public sealed class HistoryChangesUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    // Admins have the history everywhere
    protected override TestUser User => Users.Admin;

    private ILocator Sidebar => Page.Locator("#commit-sidebar");
    private ILocator Lines => Page.Locator("#commit-list .commit-line");
    private ILocator ShowNewer => Page.Locator("#commit-show-newer");

    /// <summary>Changes the file and commits it, a minute later each time. Returns the id of the commit.</summary>
    private string ChangeAndCommit(string path, string contents)
    {
        _ = Tree.File(path, contents);
        App.Time.Advance(TimeSpan.FromMinutes(1));
        HistoryService history = App.Services.GetRequiredService<HistoryService>();
        history.CommitPending(CommitReason.Idle);
        using LibGit2Sharp.Repository repository = new(history.GitDirectory);
        return repository.Head.Tip.Sha;
    }

    private Task<bool> SidebarHasFocusAsync()
        => Page.EvaluateAsync<bool>("() => document.getElementById('commit-sidebar').contains(document.activeElement)");

    // ---- Shown when asked for

    [Fact]
    public async Task The_shortcut_shows_and_focuses_then_focuses_again_then_hides_the_list()
    {
        _ = ChangeAndCommit("docs/a.md", "changed for the shortcut");
        await OpenAsync("/docs/a.md");
        await Expect(Sidebar).ToBeHiddenAsync();

        await Page.Keyboard.PressAsync("h");
        await Expect(Sidebar).ToBeVisibleAsync();
        await Expect(Lines.First).ToBeFocusedAsync();

        // The focus went back to the page: the shortcut brings it to the list again, without hiding it
        await Page.EvaluateAsync("() => document.activeElement.blur()");
        await Page.Keyboard.PressAsync("h");
        await Expect(Lines.First).ToBeFocusedAsync();
        await Expect(Sidebar).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("h");
        await Expect(Sidebar).ToBeHiddenAsync();
    }

    [Fact]
    public async Task The_button_shows_and_hides_the_list_and_escape_hides_it()
    {
        // (Without anything committed there is no history yet, and nothing to show)
        _ = ChangeAndCommit("docs/a.md", "changed for the button");
        await OpenAsync("/docs");

        await Page.Locator("#commits-button").ClickAsync();
        await Expect(Sidebar).ToBeVisibleAsync();
        // The page made room for it: the button is still there to click
        await Page.Locator("#commits-button").ClickAsync();
        await Expect(Sidebar).ToBeHiddenAsync();

        await Page.Keyboard.PressAsync("h");
        await Expect(Sidebar).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Sidebar).ToBeHiddenAsync();
        Assert.EndsWith("/docs", Page.Url);
    }

    // ---- The view of a commit

    [Fact]
    public async Task Enter_opens_a_commit_where_the_list_is_always_shown_and_the_shortcut_only_moves_the_focus()
    {
        string older = ChangeAndCommit("team/plan.md", "older plan");
        string newer = ChangeAndCommit("team/plan.md", "newer plan");
        await OpenAsync("/team/plan.md");

        await Page.Keyboard.PressAsync("h");
        await Expect(Lines.First).ToHaveAttributeAsync("data-commit", newer);
        await Expect(Lines.First).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(Lines.Nth(1)).ToBeFocusedAsync();
        await Expect(Lines.Nth(1)).ToHaveAttributeAsync("data-commit", older);

        await Page.Keyboard.PressAsync("Enter");
        await Page.WaitForURLAsync(new Regex(Regex.Escape("/team/plan.md?commit=" + older) + "$"));

        // From that commit back, with it marked, and the focus on the page
        await Expect(Sidebar).ToBeVisibleAsync();
        await Expect(Lines.First).ToHaveAttributeAsync("data-commit", older);
        await Expect(Lines.First).ToHaveAttributeAsync("aria-current", "true");
        Assert.False(await SidebarHasFocusAsync());
        await Expect(Page.Locator("#history-card-commit")).ToBeVisibleAsync();

        // The shortcut moves the focus in and out, and so does Escape out; the list stays
        await Page.Keyboard.PressAsync("h");
        await Expect(Lines.First).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("h");
        Assert.False(await SidebarHasFocusAsync());
        await Page.Keyboard.PressAsync("h");
        await Page.Keyboard.PressAsync("Escape");
        Assert.False(await SidebarHasFocusAsync());
        await Expect(Sidebar).ToBeVisibleAsync();
        Assert.EndsWith("?commit=" + older, Page.Url);

        // Its × leads back to now, and so does Escape once the focus is on the page (the card's)
        await Page.Locator("#commit-sidebar-exit").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/team/plan\\.md$"));
        await Expect(Sidebar).ToBeHiddenAsync();
        await OpenAsync("/team/plan.md?commit=" + older);
        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForURLAsync(new Regex("/team/plan\\.md$"));
    }

    [Fact]
    public async Task On_a_narrow_screen_the_view_of_a_commit_shows_the_page_and_the_list_when_asked_for()
    {
        string commit = ChangeAndCommit("docs/nested/b.md", "for the phone");
        await Page.SetViewportSizeAsync(375, 800);
        await OpenAsync("/docs/nested/b.md?commit=" + commit);

        await Expect(Sidebar).ToBeHiddenAsync();
        await Page.Locator("#commits-button").ClickAsync();
        await Expect(Sidebar).ToBeVisibleAsync();
        // Instead of the page
        await Expect(Page.Locator("#commits-button")).ToBeHiddenAsync();

        await Page.Locator("#commit-sidebar-hide").ClickAsync();
        await Expect(Sidebar).ToBeHiddenAsync();
        await Expect(Page.Locator("#commits-button")).ToBeVisibleAsync();
    }

    // ---- Newer commits

    [Fact]
    public async Task Newer_commits_can_be_added_on_top_of_the_one_that_is_shown()
    {
        string older = ChangeAndCommit("team/sub/c.md", "older c");
        string newer = ChangeAndCommit("team/sub/c.md", "newer c");
        await OpenAsync("/team/sub/c.md?commit=" + older);

        await Page.Keyboard.PressAsync("h");
        await Expect(Lines.First).ToHaveAttributeAsync("data-commit", older);
        await Expect(Lines.First).ToBeFocusedAsync();
        // The up arrow is the way to them, which holding Right Ctrl shows
        await Expect(ShowNewer).ToHaveAttributeAsync("key", new Regex("ArrowUp"));

        // Up from the top adds them, and goes to the one right above
        await Page.Keyboard.PressAsync("ArrowUp");
        await Expect(Lines.First).ToHaveAttributeAsync("data-commit", newer);
        await Expect(Lines.First).ToBeFocusedAsync();
        await Expect(Lines.Nth(1)).ToHaveAttributeAsync("data-commit", older);
        // There are no newer ones than that: the way to them is gone
        await Expect(ShowNewer).ToHaveCountAsync(0);

        // The same with the mouse
        await Page.ReloadAsync();
        await ShowNewer.ClickAsync();
        await Expect(Lines.First).ToHaveAttributeAsync("data-commit", newer);
    }

    [Fact]
    public async Task There_is_nothing_newer_than_the_newest_commit_or_now()
    {
        string newest = ChangeAndCommit("readme.md", "the newest readme");
        await OpenAsync("/readme.md?commit=" + newest);
        await Expect(Lines.First).ToHaveAttributeAsync("data-commit", newest);
        await Expect(ShowNewer).ToHaveCountAsync(0);

        await OpenAsync("/readme.md");
        await Page.Keyboard.PressAsync("h");
        await Expect(Lines.First).ToBeFocusedAsync();
        await Expect(ShowNewer).ToHaveCountAsync(0);
    }

    // ---- The arrow keys

    [Fact]
    public async Task The_arrow_keys_are_the_shortcuts_of_where_they_lead_while_the_list_has_the_focus()
    {
        _ = ChangeAndCommit("drop/existing.md", "first");
        _ = ChangeAndCommit("drop/existing.md", "second");
        await OpenAsync("/drop/existing.md");

        await Page.Keyboard.PressAsync("h");
        await Expect(Lines.First).ToBeFocusedAsync();
        await Expect(Lines.Nth(1)).ToHaveAttributeAsync("key", "ArrowDown");
        await Expect(Lines.First).Not.ToHaveAttributeAsync("key", new Regex("."));
        await Expect(Page.Locator("#commit-sidebar-hide")).ToHaveAttributeAsync("key", "Escape");

        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(Lines.First).ToHaveAttributeAsync("key", "ArrowUp");

        // Without the focus they are left to the page
        await Page.EvaluateAsync("() => document.activeElement.blur()");
        await Expect(Page.Locator("#commit-sidebar [key]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_list_goes_through_its_pages()
    {
        for (int i = 0; i <= EarlierStateCommits.PageSize; i++)
            _ = ChangeAndCommit("shelf/item.md", "version " + i);
        await OpenAsync("/shelf/item.md");

        await Page.Keyboard.PressAsync("h");
        await Expect(Lines).ToHaveCountAsync(EarlierStateCommits.PageSize);
        await ExpectDisabledWithReason(Page.Locator("#commit-newer"));

        // Down from the last line goes on to the next page, which the older button says
        await Lines.Last.FocusAsync();
        await Expect(Page.Locator("#commit-older")).ToHaveAttributeAsync("key", "ArrowDown ArrowRight");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(Page.Locator("#commit-list")).ToHaveAttributeAsync("data-page", "2");
        await Expect(Lines.First).ToBeFocusedAsync();
        await ExpectDisabledWithReason(Page.Locator("#commit-older"));

        // Left goes back, to the last line of that page
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(Page.Locator("#commit-list")).ToHaveAttributeAsync("data-page", "1");
        await Expect(Lines.Last).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Page.Locator("#commit-list")).ToHaveAttributeAsync("data-page", "2");
    }
}
