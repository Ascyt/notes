using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Integration;

/// <summary>
/// The list of the storage operations that run (<c>GET /?operations</c>, the toolbar button) and cancelling one (<c>POST /?operations=cancel</c>):
/// who sees which, who may cancel which. A zipping is held once its archive is written, so that it runs while the test asks. Each test has an
/// app of its own.
/// </summary>
public sealed class StorageOperationsTests() : IntegrationTest(new NotesAppFactory()), IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();

    /// <summary>The folder (virtual path) archived by an admin, with a file in it, readable as the permissions say.</summary>
    private async Task ArchivedAsync(string folder, JsonObject perms)
    {
        Tree.File(folder + "/note.md", "inside");
        JsonObject config = JsonNode.Parse(Cfg.Folder(perms))!.AsObject();
        config["archive"] = new JsonObject { ["enabled"] = true };
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/" + folder + "?config", config.ToJsonString()));
    }

    private (ManualResetEventSlim Reached, ManualResetEventSlim Release) HoldZippingOnceWritten()
    {
        ManualResetEventSlim reached = new();
        ManualResetEventSlim release = new();
        App.Services.GetRequiredService<FolderArchiver>().StepReached = step =>
        {
            if (step != ArchiveStep.ArchiveWritten)
                return;
            reached.Set();
            _ = release.Wait(TimeSpan.FromSeconds(30));
        };
        return (reached, release);
    }

    private async Task<JsonArray> OperationsOf(TestUser user)
    {
        HttpResponseMessage response = await Client.GetAsync(user, "/?operations");
        await AssertStatus(HttpStatusCode.OK, response);
        return JsonNode.Parse(await BodyOf(response))!.AsArray();
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                Assert.Fail("Timed out waiting until " + what);
            await Task.Delay(20, Cancellation);
        }
    }

    private Task<HttpResponseMessage> CancelAsync(TestUser user, string id)
        => Client.PostJsonAsync(user, "/?operations=cancel", new { id });

    [Fact]
    public async Task A_zipping_someone_started_is_listed_for_who_may_read_it_and_they_may_cancel_it()
    {
        await ArchivedAsync("docs/arch", Cfg.Perms());
        (ManualResetEventSlim reached, ManualResetEventSlim release) = HoldZippingOnceWritten();
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Alice, "/docs/arch?zip=entry", ""));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), Cancellation));

        JsonNode operation = Assert.Single(await OperationsOf(Users.Alice))!;
        Assert.Equal("Zipping an archived directory", (string)operation["title"]!);
        Assert.Equal("/docs/arch", (string)operation["directory"]!);
        Assert.Equal("alice", (string)operation["user"]!);
        Assert.True((bool)operation["canCancel"]!);
        Assert.Equal("writing", (string)operation["status"]!["phase"]!);
        // Someone else who may read it is shown it, but may not cancel it; who isn't logged in has no list at all
        Assert.False((bool)Assert.Single(await OperationsOf(Users.Carol))!["canCancel"]!);
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Anonymous, "/?operations"));

        await AssertStatus(HttpStatusCode.OK, await CancelAsync(Users.Alice, (string)operation["id"]!));
        release.Set();

        // Stopped: it stays unzipped, as before
        await Eventually(() => Archives.ZipProgressOf(Tree.Physical("docs/arch")) is null, "the zipping ended");
        Assert.NotNull(Archives.MountedAt(Tree.Physical("docs/arch")));
        Assert.Empty(await OperationsOf(Users.Alice));
    }

    [Fact]
    public async Task What_the_app_zips_in_a_folder_alice_may_not_read_is_not_hers_to_see_and_only_admins_cancel_it()
    {
        await ArchivedAsync("vault", Cfg.Perms(inherit: false, rwAllow: ["u:carol"], cdAllow: ["u:carol"]));
        (ManualResetEventSlim reached, ManualResetEventSlim release) = HoldZippingOnceWritten();
        // (As the job that zips idle ones does: no request, so the app's)
        Task<bool> zipping = Task.Run(() => Archives.ZipAsync(Tree.Physical("vault")), Cancellation);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), Cancellation));

        Assert.Empty(await OperationsOf(Users.Alice));
        JsonNode operation = Assert.Single(await OperationsOf(Users.Carol))!;
        Assert.Equal("the app", (string)operation["user"]!);
        Assert.False((bool)operation["canCancel"]!);
        string id = (string)operation["id"]!;
        await AssertStatus(HttpStatusCode.Forbidden, await CancelAsync(Users.Carol, id));
        await AssertStatus(HttpStatusCode.NotFound, await CancelAsync(Users.Alice, id));
        await AssertStatus(HttpStatusCode.OK, await CancelAsync(Users.Admin, id));
        release.Set();

        Assert.False(await zipping);
        Assert.NotNull(Archives.MountedAt(Tree.Physical("vault")));
    }

    [Fact]
    public async Task Cancelling_one_that_isnt_running_says_so()
        => await AssertStatus(HttpStatusCode.NotFound, await CancelAsync(Users.Alice, Guid.NewGuid().ToString()));
}
