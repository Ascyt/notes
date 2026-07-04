using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Xunit;

namespace Notes.Tests.Core.Disk.Directory;

public sealed class ControllerTests : IDisposable
{
    private readonly string _rootDir;

    public ControllerTests()
    {
        _rootDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"notes-controller-tests-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_rootDir);
    }

    [Fact]
    public async Task DeleteAsync_WhenEnableTrashFalse_DeletesPermanentlyWithoutTrashService()
    {
        string childDir = System.IO.Path.Combine(_rootDir, "child");
        System.IO.Directory.CreateDirectory(childDir);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(childDir, "file.txt"), "x");

        Notes.Core.Disk.Directory.IService directoryService = CreateDirectoryService();
        await directoryService.RefreshTreeAsync();

        await directoryService.SaveModelAsync(_rootDir, new Notes.Core.Disk.Directory.Model
        {
            Name = "root",
            EnableTrash = false
        });

        var trash = new FakeTrashService();
        var controller = CreateController(directoryService, trash, new FakeNamingService("unused"));

        IActionResult actionResult = await controller.DeleteAsync("/child");

        OkObjectResult ok = Assert.IsType<OkObjectResult>(actionResult);
        Assert.False(System.IO.Directory.Exists(childDir));
        Assert.Equal(0, trash.Calls);

        bool recycled = (bool)ok.Value!.GetType().GetProperty("Recycled")!.GetValue(ok.Value)!;
        Assert.False(recycled);
    }

    [Fact]
    public async Task DeleteAsync_WhenEnableTrashTrue_UsesTrashService()
    {
        string childDir = System.IO.Path.Combine(_rootDir, "child");
        System.IO.Directory.CreateDirectory(childDir);

        Notes.Core.Disk.Directory.IService directoryService = CreateDirectoryService();
        await directoryService.RefreshTreeAsync();

        await directoryService.SaveModelAsync(_rootDir, new Notes.Core.Disk.Directory.Model
        {
            Name = "root",
            EnableTrash = true
        });

        var trash = new FakeTrashService
        {
            OnDelete = path =>
            {
                System.IO.Directory.Delete(path, recursive: true);
                return true;
            }
        };

        var controller = CreateController(directoryService, trash, new FakeNamingService("unused"));

        IActionResult actionResult = await controller.DeleteAsync("/child");

        OkObjectResult ok = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal(1, trash.Calls);
        Assert.False(System.IO.Directory.Exists(childDir));

        bool recycled = (bool)ok.Value!.GetType().GetProperty("Recycled")!.GetValue(ok.Value)!;
        Assert.True(recycled);
    }

    [Fact]
    public async Task CreateAsync_CreatesDirectoryAndConfig_AndRefreshesTree()
    {
        Notes.Core.Disk.Directory.IService directoryService = CreateDirectoryService();
        await directoryService.RefreshTreeAsync();

        var trash = new FakeTrashService();
        var naming = new FakeNamingService("new-folder");
        var controller = CreateController(directoryService, trash, naming);

        IActionResult actionResult = await controller.CreateAsync("/");

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(actionResult);

        string newDir = System.IO.Path.Combine(_rootDir, "new-folder");
        string configPath = System.IO.Path.Combine(newDir, ".config.json");

        Assert.True(System.IO.Directory.Exists(newDir));
        Assert.True(System.IO.File.Exists(configPath));

        string json = await System.IO.File.ReadAllTextAsync(configPath);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("new-folder", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal(2, doc.RootElement.EnumerateObject().Count());

        Notes.Core.Disk.Directory.Model? rootModel = await directoryService.LoadRootModelAsync(_rootDir);
        Assert.NotNull(rootModel);
        Assert.NotNull(rootModel.Subdirectories);
        Assert.True(rootModel.Subdirectories.ContainsKey("new-folder"));

        Assert.Equal(nameof(Notes.Core.Disk.Directory.Controller.GetAsync), created.ActionName);
    }

    private Notes.Core.Disk.Directory.Controller CreateController(
        Notes.Core.Disk.Directory.IService directoryService,
        Notes.Core.Disk.Trash.IService trashService,
        Notes.Core.Disk.Naming.IService namingService)
    {
        return new Notes.Core.Disk.Directory.Controller(
            new Notes.Options { Directory = _rootDir },
            directoryService,
            trashService,
            namingService);
    }

    private Notes.Core.Disk.Directory.IService CreateDirectoryService()
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

    private sealed class FakeTrashService : Notes.Core.Disk.Trash.IService
    {
        public int Calls { get; private set; }
        public Func<string, bool>? OnDelete { get; set; }

        public bool DeleteDirectory(string fullPath)
        {
            Calls++;
            return OnDelete?.Invoke(fullPath) ?? false;
        }
    }

    private sealed class FakeNamingService(string fixedName) : Notes.Core.Disk.Naming.IService
    {
        public string GetFormattedName(string dir, string? name)
        {
            return fixedName;
        }
    }
}
