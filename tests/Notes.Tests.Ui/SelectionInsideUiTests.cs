using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Notes.Features.Components.Selection;

namespace Notes.Tests.Ui;

/// <summary>A directory with something selected inside it shows a dash in its box (_SelectionBar.js), found from the selected paths alone.</summary>
public sealed class SelectionInsideUiTests(UiFixture fixture) : UiTest(fixture), IClassFixture<UiFixture>
{
    private ILocator Box(string path) => Page.Locator($"tr.entry-row[data-path='{path}'] .entry-select");

    private Task<bool> IsDashed(string path) => Box(path).EvaluateAsync<bool>("box => box.indeterminate");

    private IReadOnlyList<string> ServerSelection
        => App.Services.GetRequiredService<ISelectionService>().Get(Users.Alice.Sub!).Paths;

    private void SelectOnServer(params string[] paths)
    {
        // As the page would, without going there: the dash must come from what the selection says, not from what the page has seen
        ISelectionService selections = App.Services.GetRequiredService<ISelectionService>();
        _ = selections.Add(Users.Alice.Sub!, paths.Select(path => new SelectionEntry(path, IsDirectory: false, CanDelete: true)));
    }

    [Fact]
    public async Task Directories_above_a_selected_entry_show_a_dash_at_any_depth()
    {
        SelectOnServer("docs/nested/b.md");

        await OpenAsync("/");
        Assert.True(await IsDashed("docs"));
        Assert.False(await Box("docs").IsCheckedAsync());
        Assert.False(await IsDashed("team"));

        await OpenAsync("/docs");
        Assert.True(await IsDashed("docs/nested"));
    }

    [Fact]
    public async Task Using_a_dashed_box_unselects_everything_inside_and_using_it_again_selects_the_directory()
    {
        SelectOnServer("docs/nested/b.md", "docs/a.md");
        await OpenAsync("/");
        Assert.True(await IsDashed("docs"));

        await Box("docs").ClickAsync();

        await Eventually(() => ServerSelection.Count == 0, "everything inside is unselected");
        await Expect(Page.Locator("#selection-bar")).ToBeHiddenAsync();
        Assert.False(await IsDashed("docs"));
        Assert.False(await Box("docs").IsCheckedAsync());

        await Box("docs").ClickAsync();

        await Eventually(() => ServerSelection.SequenceEqual(["docs"]), "the directory is selected");
        await Expect(Box("docs")).ToBeCheckedAsync();
    }

    [Fact]
    public async Task Pressing_on_the_row_of_a_dashed_directory_does_the_same()
    {
        SelectOnServer("docs/nested/b.md");
        await OpenAsync("/docs");

        await Page.Locator("tr.entry-row[data-path='docs/nested'] td").Last.ClickAsync();

        await Eventually(() => ServerSelection.Count == 0, "everything inside is unselected");
        Assert.False(await IsDashed("docs/nested"));
    }

    [Fact]
    public async Task Unselecting_what_is_inside_takes_the_dash_away()
    {
        SelectOnServer("docs/a.md");
        await OpenAsync("/");
        Assert.True(await IsDashed("docs"));

        await Page.Locator("#selection-clear").ClickAsync();

        await Expect(Page.Locator("#selection-bar")).ToBeHiddenAsync();
        Assert.False(await IsDashed("docs"));
    }
}
