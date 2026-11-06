using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using notes.Features.Components.Selection;

namespace notes.Tests.Ui;

/// <summary>
/// The app on a real port (Kestrel), and a real browser. The browser is the Edge (or Chrome) installed on the machine, so Playwright doesn't
/// need to download one; set NOTES_UI_BROWSER to "chrome" or "chromium" to use another. Without any, the tests are skipped.
/// </summary>
public class UiFixture : IAsyncLifetime
{
    public NotesAppFactory App { get; } = new();
    public string BaseUrl { get; private set; } = "";
    public IBrowser? Browser { get; private set; }
    public string? Unavailable { get; private set; }
    private IPlaywright? _playwright;

    public async ValueTask InitializeAsync()
    {
        App.UseKestrel(0);
        App.StartServer();
        BaseUrl = App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().Replace("[::]", "localhost").Replace("0.0.0.0", "localhost");

        try
        {
            _playwright = await Playwright.CreateAsync();
            string channel = Environment.GetEnvironmentVariable("NOTES_UI_BROWSER") ?? (OperatingSystem.IsWindows() ? "msedge" : "chrome");
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = channel == "chromium" ? null : channel, Headless = Environment.GetEnvironmentVariable("NOTES_UI_HEADED") is null });
        }
        catch (PlaywrightException e)
        {
            Unavailable = "No browser for the UI tests: " + e.Message.Split('\n')[0];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
        await App.DisposeAsync();
    }
}

/// <summary>A page of the app in the browser, as one of the <see cref="Users"/>, on the <see cref="BaseTree"/> (fresh for every test).</summary>
public abstract class UiTest : IAsyncLifetime
{
    protected UiTest(UiFixture fixture)
    {
        Fixture = fixture;
    }

    protected UiFixture Fixture { get; }
    protected NotesAppFactory App => Fixture.App;
    protected NotesTree Tree => App.Tree;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    /// <summary>Who the browser is; change before <see cref="OpenAsync"/>.</summary>
    protected virtual TestUser User => Users.Alice;

    public async ValueTask InitializeAsync()
    {
        if (Fixture.Unavailable is { } reason)
            Assert.Skip(reason);

        App.ResetNotes();
        BaseTree.Create(Tree);
        ISelectionService selections = App.Services.GetRequiredService<ISelectionService>();
        foreach (TestUser user in new[] { Users.Admin, Users.Alice, Users.Bob, Users.Carol })
            selections.EndTransfer(user.Sub!, clear: true);

        Context = await NewContextAsync(User);
        Page = await Context.NewPageAsync();
    }

    protected async Task<IBrowserContext> NewContextAsync(TestUser user)
    {
        Dictionary<string, string> headers = [];
        if (user.ToHeader() is { } header)
            headers[TestUser.HeaderName] = header;
        IBrowserContext context = await Fixture.Browser!.NewContextAsync(new BrowserNewContextOptions { BaseURL = Fixture.BaseUrl, ExtraHTTPHeaders = headers });
        context.SetDefaultTimeout(10_000);
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        if (Context is not null)
            await Context.DisposeAsync();
    }

    protected async Task OpenAsync(string path)
    {
        IResponse? response = await Page.GotoAsync(path);
        Assert.True(response?.Ok, $"{path}: {response?.Status}");
    }

    /// <summary>The newest message of the toast stack.</summary>
    protected ILocator Toast => Page.Locator(".toast-card").First;

    protected static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    protected static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    /// <summary>Waits until the file system shows what the page did (it doesn't wait for the server by itself).</summary>
    protected static async Task Eventually(Func<bool> condition, string what)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                Assert.Fail("Timed out waiting until " + what);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Waits for the Monaco editor of the page.</summary>
    protected async Task WaitForEditorAsync()
        => await Page.WaitForFunctionAsync("() => window.editor && typeof window.editor.getValue === 'function'"); // (window.editor alone is also the element with that id)
}
