using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using notes.Features.History;

namespace notes.Tests.Ui;

/// <summary>
/// The scripts of the changes of a commit (<c>?commit=&amp;changes</c>): the compare button of the list of changes and its Shift+Enter, the
/// diff editor (file-changes.js), the card keeping the changes on the links, and the way between the state and the changes.
/// </summary>
public sealed class HistoryCommitChangesUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    // Admins have the history everywhere
    protected override TestUser User => Users.Admin;

    private ILocator Rows => Page.Locator("#commit-list .commit-row");

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

    private async Task WaitForDiffEditorAsync()
        => await Page.WaitForFunctionAsync("() => window.changesEditor && typeof window.changesEditor.getOriginalEditor === 'function'");

    [Fact]
    public async Task Shift_enter_on_a_line_of_the_list_opens_the_changes_of_its_commit()
    {
        _ = ChangeAndCommit("team/plan.md", "older plan");
        string newer = ChangeAndCommit("team/plan.md", "newer plan");
        await OpenAsync("/team/plan.md");

        await Page.Keyboard.PressAsync("h");
        // The compare button is there on the line that has the focus, and has Shift+Enter
        await Expect(Rows.First.Locator(".commit-compare")).ToBeVisibleAsync();
        await Expect(Rows.Nth(1).Locator(".commit-compare")).ToBeHiddenAsync();
        await Expect(Rows.First.Locator(".commit-compare")).ToHaveAttributeAsync("key", "+Enter");
        await Expect(Rows.Nth(1).Locator(".commit-compare")).Not.ToHaveAttributeAsync("key", new Regex(".*"));

        await Page.Keyboard.PressAsync("Shift+Enter");
        await Page.WaitForURLAsync(new Regex(Regex.Escape("/team/plan.md?commit=" + newer + "&changes") + "$"));

        await WaitForDiffEditorAsync();
        Assert.Equal("older plan", await Page.EvaluateAsync<string>("() => window.changesEditor.getOriginalEditor().getValue()"));
        Assert.Equal("newer plan", await Page.EvaluateAsync<string>("() => window.changesEditor.getModifiedEditor().getValue()"));
        Assert.True(await Page.EvaluateAsync<bool>("() => window.changesEditor.getModifiedEditor().getOption(monaco.editor.EditorOption.readOnly)"));
        // The list is shown as in the view of a commit, with it marked
        await Expect(Page.Locator("#commit-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator("#commit-list .commit-line[aria-current]")).ToHaveAttributeAsync("data-commit", newer);
        await Expect(Page.Locator("#history-card")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_links_keep_the_changes_and_shift_h_goes_between_them_and_the_state()
    {
        string commit = ChangeAndCommit("docs/a.md", "changed for the links");
        string changes = "?commit=" + commit + "&changes";
        await OpenAsync("/docs" + changes);

        ILocator entry = Page.Locator("a[href^='/docs/a.md']");
        await Expect(entry).ToHaveAttributeAsync("href", "/docs/a.md" + changes);
        await entry.ClickAsync();
        await Page.WaitForURLAsync(new Regex(Regex.Escape("/docs/a.md" + changes) + "$"));
        await WaitForDiffEditorAsync();

        // To the state of the commit and back
        await Page.Keyboard.PressAsync("Shift+H");
        await Page.WaitForURLAsync(new Regex(Regex.Escape("/docs/a.md?commit=" + commit) + "$"));
        await Expect(Page.Locator("#markdown-preview")).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Shift+H");
        await Page.WaitForURLAsync(new Regex(Regex.Escape("/docs/a.md" + changes) + "$"));

        // The card's × leads to now, without the commit or its changes
        await Page.Locator("#history-exit").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/docs/a\\.md$"));
    }
}
