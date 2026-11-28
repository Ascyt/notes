using Microsoft.Extensions.DependencyInjection;
using Notes.Features.Configuration;

namespace Notes.Tests.Integration;

/// <summary>
/// A request looks at no configuration on disk that the app knows already (principles.md, 22): which folders have one is in the index, and
/// what each says is read once, until the app changes it. A configuration it doesn't know yet is read once per request, however many checks
/// ask about it.
/// </summary>
public sealed class RequestDiskReadsTests(NotesAppFactory app) : IntegrationTest(app)
{
    private FolderConfigStore Configs => App.Services.GetRequiredService<FolderConfigStore>();

    private async Task<long> ConfigReadsOf(TestUser user, string address)
    {
        long before = Configs.DiskReads;
        await AssertStatus(System.Net.HttpStatusCode.OK, await Client.GetAsync(user, address));
        return Configs.DiskReads - before;
    }

    [Fact]
    public async Task A_file_five_directories_down_reads_each_configuration_on_its_way_once_and_then_none()
    {
        Tree.File("a/b/c/d/e/note.md", "deep");
        foreach (string folder in new[] { "a", "a/b", "a/b/c", "a/b/c/d", "a/b/c/d/e" })
            Tree.Config(folder, Cfg.Folder(Cfg.Perms()));

        // The root's was read before (by the test's own requests or not); the five below it are new to the app
        Assert.InRange(await ConfigReadsOf(Users.Alice, "/a/b/c/d/e/note.md"), 5, 6);
        Assert.Equal(0, await ConfigReadsOf(Users.Alice, "/a/b/c/d/e/note.md"));
        Assert.Equal(0, await ConfigReadsOf(Users.Admin, "/a/b/c/d/e/note.md"));
    }

    [Fact]
    public async Task A_directory_reads_those_of_the_directories_it_lists_once_and_then_none()
    {
        foreach (string folder in new[] { "a/b/x", "a/b/y", "a/b/z" })
        {
            Tree.File(folder + "/one.md", "x");
            Tree.Config(folder, Cfg.Folder(Cfg.Perms()));
        }
        // One without a configuration of its own is never looked at
        Tree.File("a/b/plain/one.md", "p");

        Assert.InRange(await ConfigReadsOf(Users.Alice, "/a/b"), 3, 4);
        Assert.Equal(0, await ConfigReadsOf(Users.Alice, "/a/b"));
        Assert.Equal(0, await ConfigReadsOf(Users.Admin, "/a/b"));
    }
}
