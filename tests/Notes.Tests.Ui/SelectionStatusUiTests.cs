using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Notes.Features.Components.Selection;
using Notes.Features.Passwords;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Ui;

/// <summary>
/// What the selection card offers for what is selected, as it is for this browser (SelectionStatus): Unlock for what a password locks (the
/// only thing it offers then), Unzip for zipped archived folders, Zip and Lock for what is unzipped or unlocked. Each test has entries of its
/// own, as the app keeps which archived folders are unzipped across the tests.
/// </summary>
public sealed class SelectionStatusUiTests(NoWaitForUnzippingFixture fixture) : UiTest(fixture), IClassFixture<NoWaitForUnzippingFixture>
{
    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();
    private ILocator Box(string path) => Page.Locator($".entry-row[data-path='{path}'] .entry-select");
    private ILocator Count => Page.Locator("#selection-count");

    private IReadOnlyList<string> ServerSelection
        => App.Services.GetRequiredService<ISelectionService>().Get(Users.Alice.Sub!).Paths;

    private void LockedFolder(string name, string password)
    {
        Tree.File($"docs/{name}/x.md", "x");
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        config["passwordHash"] = PasswordHashing.Hash(password, 4);
        Tree.Config($"docs/{name}", config.ToJsonString());
    }

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

    private async Task SelectAsync(params string[] paths)
    {
        foreach (string path in paths)
            await Box(path).CheckAsync();
        await Eventually(() => paths.All(ServerSelection.Contains), "the server has the selection");
        await Expect(Count).ToHaveAttributeAsync("data-count", paths.Length.ToString());
    }

    [Fact]
    public async Task What_a_password_locks_can_be_selected_and_unlocked_from_the_card_and_locked_again()
    {
        LockedFolder("locked-ui", "pw");
        await OpenAsync("/docs");

        await SelectAsync("docs/locked-ui", "docs/a.md");

        await Expect(Count).ToHaveTextAsync("1 password-protected element selected (+1 other)");
        // Nothing else can be done with it until then
        await Expect(Page.Locator("#selection-delete")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-disabled"));
        await Expect(Page.Locator("#selection-unlock")).ToHaveTextAsync("Unlock...");
        await Page.Locator("#selection-unlock").ClickAsync();
        await Expect(Page.Locator("#password-modal")).ToBeVisibleAsync();
        await Page.Locator("#password-input").FillAsync("pw");
        await Page.Locator("#password-confirm").ClickAsync();

        // Loaded again, with the selection as it was
        await Expect(Count).ToHaveTextAsync("2 elements selected");
        Assert.Equal(["docs/a.md", "docs/locked-ui"], ServerSelection);
        await Expect(Page.Locator("#selection-unlock")).ToBeHiddenAsync();
        await Expect(Page.Locator("#selection-delete")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-disabled"));

        await Page.Locator("#selection-lock").ClickAsync();

        await Expect(Count).ToHaveTextAsync("1 password-protected element selected (+1 other)");
        Assert.Equal(["docs/a.md", "docs/locked-ui"], ServerSelection);
    }

    [Fact]
    public async Task Two_passwords_are_asked_for_one_after_the_other()
    {
        LockedFolder("locked-one-ui", "one");
        LockedFolder("locked-two-ui", "two");
        await OpenAsync("/docs");
        await SelectAsync("docs/locked-one-ui", "docs/locked-two-ui");

        await Expect(Page.Locator("#selection-unlock")).ToHaveTextAsync("Unlock all...");
        await Page.Locator("#selection-unlock").ClickAsync();
        await Expect(Page.Locator("#password-entry")).ToContainTextAsync("locked-one-ui");
        await Page.Locator("#password-input").FillAsync("one");
        await Page.Locator("#password-confirm").ClickAsync();
        await Expect(Page.Locator("#password-entry")).ToContainTextAsync("locked-two-ui");
        await Page.Locator("#password-input").FillAsync("two");
        await Page.Locator("#password-confirm").ClickAsync();

        await Expect(Count).ToHaveTextAsync("2 elements selected");
        await Expect(Page.Locator("#selection-lock")).ToHaveTextAsync("Lock all");
    }

    [Fact]
    public async Task An_unzipped_archived_folder_can_be_zipped_from_the_card_and_unzipped_again()
    {
        string folder = await ArchivedFolderAsync("archived-card-ui");
        await OpenAsync("/docs");
        await SelectAsync("docs/archived-card-ui");

        await Expect(Page.Locator("#selection-unzip")).ToBeHiddenAsync();
        await Page.Locator("#selection-zip").ClickAsync();
        await Eventually(() => Archives.IsZipped(folder) && Archives.ZipProgressOf(folder) is null, "it is zipped");

        _ = await Page.ReloadAsync();
        // (A reload of one's own clears the selection: selected again)
        await SelectAsync("docs/archived-card-ui");
        await Expect(Page.Locator("#selection-zip")).ToBeHiddenAsync();
        await Expect(Page.Locator("#selection-unzip")).ToHaveTextAsync("Unzip");
        await Page.Locator("#selection-unzip").ClickAsync();

        await Eventually(() => Archives.MountedAt(folder) is not null, "it is unzipped");
        await Expect(Page.Locator("#selection-zip")).ToBeVisibleAsync();
        Assert.Equal(["docs/archived-card-ui"], ServerSelection);
    }
}
