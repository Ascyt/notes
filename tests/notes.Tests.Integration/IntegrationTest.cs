using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using notes.Features.Components.Selection;

namespace notes.Tests.Integration;

/// <summary>The app with the <see cref="BaseTree"/>, fresh for every test (the app itself is shared by the tests of a class).</summary>
public abstract class IntegrationTest : IClassFixture<NotesAppFactory>
{
    protected IntegrationTest(NotesAppFactory app)
    {
        App = app;
        App.ResetNotes();
        BaseTree.Create(Tree);
        Client = new NotesClient(app);

        // The selections of the last test of this class are still in memory
        ISelectionService selections = app.Services.GetRequiredService<ISelectionService>();
        foreach (TestUser user in new[] { Users.Admin, Users.Alice, Users.Bob, Users.Carol, Users.Nobody })
        {
            selections.EndTransfer(user.Sub!, clear: true);
        }
    }

    protected NotesAppFactory App { get; }

    protected NotesTree Tree => App.Tree;

    protected NotesClient Client { get; }

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected static async Task<string> BodyOf(HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    protected static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
            Assert.Fail($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}: {Truncate(await BodyOf(response))}");
    }

    protected static string Truncate(string text) => text.Length > 400 ? text[..400] + "..." : text;

    /// <summary>The names of the entries of a zip download.</summary>
    protected static async Task<List<string>> ZipNamesAsync(HttpResponseMessage response)
    {
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(Cancellation);
        using System.IO.Compression.ZipArchive zip = new(new MemoryStream(bytes));
        return zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
    }

    // ---- Selections and transfers, as the selection card does them

    protected async Task<SelectionSnapshotDto> SelectAsync(TestUser user, params string[] paths)
    {
        HttpResponseMessage response = await Client.PostJsonAsync(user, "/?selection=add", new { paths });
        await AssertStatus(HttpStatusCode.OK, response);
        return (await response.Content.ReadFromJsonAsync<SelectionSnapshotDto>(Cancellation))!;
    }

    protected async Task<SelectionSnapshotDto> GetSelectionAsync(TestUser user)
    {
        HttpResponseMessage response = await Client.GetAsync(user, "/?selection");
        await AssertStatus(HttpStatusCode.OK, response);
        return (await response.Content.ReadFromJsonAsync<SelectionSnapshotDto>(Cancellation))!;
    }

    /// <summary>Selects the paths and transfers them into the directory (kind copy, move or delete).</summary>
    protected async Task<HttpResponseMessage> TransferAsync(TestUser user, string kind, string targetDirectory, params string[] paths)
    {
        SelectionSnapshotDto selection = await SelectAsync(user, paths);
        return await Client.PostJsonAsync(user, "/" + targetDirectory + "?transfer=" + kind, new { version = selection.Version });
    }
}

public sealed record SelectionSnapshotDto(long Version, bool Busy, string[] Paths, int DirectoryCount, bool CanDelete);
