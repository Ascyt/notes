using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Notes.Tests.Core.Disk.Directory;

public class ControllerTests
{
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NotesTests_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Path))
                {
                    System.IO.Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed class PermanentTrashService : Notes.Core.Disk.Trash.IService
    {
        public bool DeleteDirectory(string fullPath)
        {
            System.IO.Directory.Delete(fullPath, recursive: true);
            return false;
        }
    }

    private static Notes.Core.Disk.Directory.Controller CreateController(string rootDir)
    {
        Options options = new() { Directory = rootDir };
        return new Notes.Core.Disk.Directory.Controller(options, new PermanentTrashService());
    }

    [Fact]
    public void Get_Returns500_WhenRootDirectoryNotConfigured()
    {
        Options options = new() { Directory = "" };
        Notes.Core.Disk.Directory.Controller controller = new(options, new PermanentTrashService());

        IActionResult result = controller.Get("/");

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public void Get_ReturnsBadRequest_WhenPathDoesNotStartWithSlash()
    {
        using TempDir root = new();
        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Get("sub");

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("must start with `/`", bad.Value?.ToString());
    }

    [Fact]
    public void Get_ReturnsBadRequest_WhenResolvedPathEscapesRoot()
    {
        using TempDir root = new();
        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Get("/../outside");

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("escapes the configured root", bad.Value?.ToString());
    }

    [Fact]
    public void Get_ReturnsNotFound_ForMissingDirectory()
    {
        using TempDir root = new();
        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Get("/does-not-exist");

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("Not found:", notFound.Value?.ToString());
    }

    [Fact]
    public void Get_ReturnsUnprocessable_WhenPathPointsToFile()
    {
        using TempDir root = new();
        string filePath = System.IO.Path.Combine(root.Path, "a.txt");
        File.WriteAllText(filePath, "hello");

        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Get("/a.txt");

        UnprocessableEntityObjectResult unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Contains("points to a file", unprocessable.Value?.ToString());
    }

    [Fact]
    public void Get_ReturnsDirectoryListing_ForRoot()
    {
        using TempDir root = new();
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "child"));
        File.WriteAllText(System.IO.Path.Combine(root.Path, "a.txt"), "hello");

        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Get("/");

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        // Anonymous type; validate via reflection to avoid coupling.
        object payload = ok.Value!;
        string virtualPath = (string)payload.GetType().GetProperty("VirtualPath")!.GetValue(payload)!;
        string physicalPath = (string)payload.GetType().GetProperty("PhysicalPath")!.GetValue(payload)!;
        object itemsObj = payload.GetType().GetProperty("Items")!.GetValue(payload)!;

        Assert.Equal("/", virtualPath);
        Assert.Equal(System.IO.Path.GetFullPath(root.Path), physicalPath);

        IEnumerable<object> items = Assert.IsAssignableFrom<IEnumerable<object>>(itemsObj);
        var names = items.Select(i => (string)i.GetType().GetProperty("Name")!.GetValue(i)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("child", names);
        Assert.Contains("a.txt", names);
    }

    [Fact]
    public void Create_RefusesRoot()
    {
        using TempDir root = new();
        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Create("/");

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Refusing to create the root", bad.Value?.ToString());
    }

    [Fact]
    public void Create_ReturnsConflict_WhenDirectoryAlreadyExists()
    {
        using TempDir root = new();
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "child"));

        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Create("/child");

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("already exists", conflict.Value?.ToString());
    }

    [Fact]
    public void Create_ReturnsUnprocessable_WhenPathPointsToFile()
    {
        using TempDir root = new();
        File.WriteAllText(System.IO.Path.Combine(root.Path, "a.txt"), "hello");

        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Create("/a.txt");

        UnprocessableEntityObjectResult unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Contains("points to a file", unprocessable.Value?.ToString());
    }

    [Fact]
    public void Create_CreatesDirectory_AndReturnsCreatedAtAction()
    {
        using TempDir root = new();
        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Create("/newdir");

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(Notes.Core.Disk.Directory.Controller.Get), created.ActionName);

        Assert.NotNull(created.Value);
        object payload = created.Value!;
        string virtualPath = (string)payload.GetType().GetProperty("VirtualPath")!.GetValue(payload)!;
        string physicalPath = (string)payload.GetType().GetProperty("PhysicalPath")!.GetValue(payload)!;

        Assert.Equal("/newdir", virtualPath);
        Assert.True(System.IO.Directory.Exists(System.IO.Path.Combine(root.Path, "newdir")));
        Assert.Equal(System.IO.Path.GetFullPath(System.IO.Path.Combine(root.Path, "newdir")), physicalPath);
    }

    [Fact]
    public void Delete_RefusesRoot()
    {
        using TempDir root = new();
        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Delete("/");

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Refusing to delete the root", bad.Value?.ToString());
    }

    [Fact]
    public void Delete_ReturnsNotFound_ForMissingDirectory()
    {
        using TempDir root = new();
        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Delete("/missing");

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("Not found:", notFound.Value?.ToString());
    }

    [Fact]
    public void Delete_ReturnsUnprocessable_WhenPathPointsToFile()
    {
        using TempDir root = new();
        File.WriteAllText(System.IO.Path.Combine(root.Path, "a.txt"), "hello");

        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Delete("/a.txt");

        UnprocessableEntityObjectResult unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Contains("points to a file", unprocessable.Value?.ToString());
    }

    [Fact]
    public void Delete_DeletesDirectory_AndReturnsOk()
    {
        using TempDir root = new();
        string targetDir = System.IO.Path.Combine(root.Path, "todelete");
        System.IO.Directory.CreateDirectory(targetDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(targetDir, "a.txt"), "hello");

        Notes.Core.Disk.Directory.Controller controller = CreateController(root.Path);

        IActionResult result = controller.Delete("/todelete");

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.False(System.IO.Directory.Exists(targetDir));

        Assert.NotNull(ok.Value);
        object payload = ok.Value!;
        bool recycled = (bool)payload.GetType().GetProperty("Recycled")!.GetValue(payload)!;
        Assert.False(recycled);
    }
}
