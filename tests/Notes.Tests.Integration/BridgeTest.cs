using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Notes.Features.Components.Selection;

namespace Notes.Tests.Integration;

/// <summary>
/// The app with the bridges of <see cref="BridgedNotesAppFactory"/>, and these notes, fresh for every test:
/// <code>
/// /                 team: read/write, create/delete; guests, anonymous, carol: read      (inherit: false)
///   readme.md
///   hdd/            bridge (no configuration: as the root)
///     test/note.md
///     archive/      bridge in a bridge, team only                                        (inherit: false)
///       old.md
///   vault/          carol only                                                            (inherit: false)
///     deep/         bridge (no configuration: as vault)
///       secret.md
/// </code>
/// On disk, the root also has <c>hdd/shadowed.md</c>, which the bridge hides.
/// </summary>
public abstract class BridgeTest : IClassFixture<BridgedNotesAppFactory>
{
    protected BridgeTest(BridgedNotesAppFactory app)
    {
        App = app;
        App.ResetBridgedNotes();

        App.Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["g:guests", "a", "u:carol"], cdAllow: ["g:team"], historyAllow: ["g:team"])));
        _ = App.Tree.File("readme.md", "# Readme");
        _ = App.Tree.File("hdd/shadowed.md", "hidden by the bridge");
        _ = App.Hdd.File("test/note.md", "note");
        App.Archive.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"])));
        _ = App.Archive.File("old.md", "old");
        App.Tree.Config("vault", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["u:carol"], cdAllow: ["u:carol"])));
        _ = App.Deep.File("secret.md", "the secret");

        // (The app starts with the first client, once the directories the bridges are mounted in are there)
        Client = new NotesClient(app);

        // The selections of the last test of this class are still in memory
        ISelectionService selections = app.Services.GetRequiredService<ISelectionService>();
        foreach (TestUser user in new[] { Users.Admin, Users.Alice, Users.Bob, Users.Carol, Users.Nobody })
            selections.EndTransfer(user.Sub!, clear: true);
    }

    protected BridgedNotesAppFactory App { get; }

    protected NotesClient Client { get; }

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected static async Task<string> BodyOf(HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync(Cancellation);

    protected static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
        {
            string body = await BodyOf(response);
            Assert.Fail($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}: {(body.Length > 400 ? body[..400] : body)}");
        }
    }

    /// <summary>Selects the paths and transfers them into the directory (kind copy, move or delete).</summary>
    protected async Task<HttpResponseMessage> TransferAsync(TestUser user, string kind, string targetDirectory, params string[] paths)
    {
        HttpResponseMessage selected = await Client.PostJsonAsync(user, "/?selection=add", new { paths });
        await AssertStatus(HttpStatusCode.OK, selected);
        SelectionSnapshotDto selection = (await selected.Content.ReadFromJsonAsync<SelectionSnapshotDto>(Cancellation))!;
        return await Client.PostJsonAsync(user, "/" + targetDirectory + "?transfer=" + kind, new { version = selection.Version });
    }

    /// <summary>The names of the entries of a zip download.</summary>
    protected static async Task<List<string>> ZipNamesAsync(HttpResponseMessage response)
    {
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(Cancellation);
        using System.IO.Compression.ZipArchive zip = new(new MemoryStream(bytes));
        return [.. zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
    }
}
