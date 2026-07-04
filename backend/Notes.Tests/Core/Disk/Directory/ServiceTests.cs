using System.Text.Json;
using Xunit;

namespace Notes.Tests.Core.Disk.Directory;

public sealed class ServiceTests : IDisposable
{
    private readonly string _rootDir;

    public ServiceTests()
    {
        _rootDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"notes-tests-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_rootDir);
    }

    [Fact]
    public async Task RefreshTreeAsync_CreatesConfigsForAllDirectories_AndBuildsTree()
    {
        string subA = System.IO.Path.Combine(_rootDir, "subA");
        string subB = System.IO.Path.Combine(subA, "subB");

        System.IO.Directory.CreateDirectory(subB);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(_rootDir, "root.txt"), "r");
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(subA, "a.txt"), "a");

        var service = CreateService();

        await service.RefreshTreeAsync();

        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(_rootDir, ".config.json")));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(subA, ".config.json")));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(subB, ".config.json")));

        Notes.Core.Disk.Directory.Model? rootModel = await service.LoadRootModelAsync(_rootDir);
        Assert.NotNull(rootModel);
        Assert.Equal(System.IO.Path.GetFileName(_rootDir), rootModel.Name);

        Assert.NotNull(rootModel.Files);
        Assert.True(rootModel.Files.ContainsKey("root.txt"));

        Assert.NotNull(rootModel.Subdirectories);
        Assert.True(rootModel.Subdirectories.TryGetValue("subA", out Notes.Core.Disk.Directory.Model? subAModel));
        Assert.NotNull(subAModel);

        Assert.NotNull(subAModel!.Files);
        Assert.True(subAModel.Files.ContainsKey("a.txt"));

        Assert.NotNull(subAModel.Subdirectories);
        Assert.True(subAModel.Subdirectories.TryGetValue("subB", out Notes.Core.Disk.Directory.Model? subBModel));
        Assert.NotNull(subBModel);
    }

    [Fact]
    public async Task SaveModelAsync_WritesMinimalConfigOnly()
    {
        var service = CreateService();

        await service.SaveModelAsync(_rootDir, new Notes.Core.Disk.Directory.Model
        {
            Name = "RootName",
            EnableTrash = true,
            Files = [],
            Subdirectories = []
        });

        string json = await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine(_rootDir, ".config.json"));
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement root = document.RootElement;
        Assert.True(root.TryGetProperty("Name", out JsonElement nameElement));
        Assert.Equal("RootName", nameElement.GetString());

        Assert.True(root.TryGetProperty("EnableTrash", out JsonElement enableTrashElement));
        Assert.True(enableTrashElement.GetBoolean());

        Assert.Equal(2, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task ResolveEnableTrashAsync_UsesClosestAncestorWithValue()
    {
        string sub1 = System.IO.Path.Combine(_rootDir, "sub1");
        string sub2 = System.IO.Path.Combine(sub1, "sub2");

        System.IO.Directory.CreateDirectory(sub2);

        var service = CreateService();

        await service.RefreshTreeAsync();

        await service.SaveModelAsync(_rootDir, new Notes.Core.Disk.Directory.Model
        {
            Name = "root",
            EnableTrash = true
        });

        await service.SaveModelAsync(sub1, new Notes.Core.Disk.Directory.Model
        {
            Name = "sub1",
            EnableTrash = null
        });

        await service.SaveModelAsync(sub2, new Notes.Core.Disk.Directory.Model
        {
            Name = "sub2",
            EnableTrash = false
        });

        Assert.True(await service.ResolveEnableTrashAsync(_rootDir));
        Assert.True(await service.ResolveEnableTrashAsync(sub1));
        Assert.False(await service.ResolveEnableTrashAsync(sub2));

        await service.SaveModelAsync(sub2, new Notes.Core.Disk.Directory.Model
        {
            Name = "sub2",
            EnableTrash = null
        });

        await service.SaveModelAsync(sub1, new Notes.Core.Disk.Directory.Model
        {
            Name = "sub1",
            EnableTrash = false
        });

        Assert.False(await service.ResolveEnableTrashAsync(sub2));
    }

    private Notes.Core.Disk.Directory.Service CreateService()
    {
        return new Notes.Core.Disk.Directory.Service(new Notes.Options
        {
            Directory = _rootDir
        });
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(_rootDir))
            {
                System.IO.Directory.Delete(_rootDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures on Windows file locking edge-cases.
        }
    }
}
