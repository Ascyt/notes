using notes.Features.Helpers;

namespace notes.Tests.Unit;

public sealed class PathServiceTests : IDisposable
{
    private readonly TempNotes _notes = new();
    private readonly PathService _paths = new();

    public void Dispose() => _notes.Dispose();

    [Theory]
    [InlineData("a.md")]
    [InlineData("with space.md")]
    [InlineData("ümlaut and emoji 🎉.md")]
    [InlineData(".hidden")]
    [InlineData("..md")]
    [InlineData("_a")]
    [InlineData("config.json")]
    public void Valid_names(string name) => Assert.True(PathService.IsValid(name));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" a")]
    [InlineData("a ")]
    [InlineData("a\t")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("_")]
    [InlineData(".config.json")]
    [InlineData(".CONFIG.JSON")]
    [InlineData(".Config.Json")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../a")]
    [InlineData("..\\a")]
    [InlineData("a\0b")]
    [InlineData("a.")]
    [InlineData("a.md.")]
    [InlineData(".config.json.")]
    [InlineData("_.")]
    public void Invalid_names(string name) => Assert.False(PathService.IsValid(name));

    [Fact]
    public void The_root_is_the_empty_path()
        => Assert.Equal(Path.GetFullPath(_notes.Root), _paths.ToPhysicalPath(_notes.Root, ""));

    [Fact]
    public void Finds_existing_entries_case_insensitively_and_returns_their_real_spelling()
    {
        _notes.Files.File("Dir/File.md");

        Assert.Equal(_notes.P("Dir/File.md"), _paths.ToPhysicalPath(_notes.Root, "dir/file.MD"));
        Assert.Equal(_notes.P("Dir/File.md"), _paths.ToPhysicalPath(_notes.Root, "/Dir//File.md/"));
    }

    [Fact]
    public void Missing_entries_have_no_path()
        => Assert.Null(_paths.ToPhysicalPath(_notes.Root, "missing.md"));

    [Theory]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("dir/../../outside.txt")]
    [InlineData("dir/..")]
    [InlineData("./dir")]
    [InlineData("dir/./file.md")]
    [InlineData("_")]
    [InlineData("_/temp")]
    [InlineData(".config.json")]
    [InlineData("dir/.config.json")]
    [InlineData("dir/.CONFIG.json")]
    [InlineData("dir/file.md\0.txt")]
    [InlineData("dir/file.md::$DATA")]
    [InlineData("dir/file.md:stream")]
    [InlineData("dir/file.md.")]
    [InlineData("dir/file.md ")]
    [InlineData("dir/ file.md")]
    public void Paths_that_lead_out_or_to_reserved_entries_have_no_path(string virtualPath)
    {
        _notes.Files.File("dir/file.md");
        _notes.Files.Config("dir", "{}");
        _notes.Files.File("_/temp/x");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_notes.Root)!, "outside.txt"), "outside");

        Assert.Null(_paths.ToPhysicalPath(_notes.Root, virtualPath));
    }

    [Theory]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("//server/share/x")]
    [InlineData("/etc/passwd")]
    [InlineData("~/x")]
    [InlineData("%2e%2e/x")]
    public void Absolute_and_foreign_paths_have_no_path(string virtualPath)
        => Assert.Null(_paths.ToPhysicalPath(_notes.Root, virtualPath));

    [Fact]
    public void Links_are_not_followed()
    {
        string outside = Path.Combine(Path.GetTempPath(), "notes-unit-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(_notes.P("link"), outside);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Symbolic links can't be made here (on Windows they need developer mode or admin rights).");
            }

            Assert.Null(_paths.ToPhysicalPath(_notes.Root, "link"));
            Assert.Null(_paths.ToPhysicalPath(_notes.Root, "link/secret.txt"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Virtual_paths_use_slashes()
    {
        Assert.Equal("a/b.md", _paths.ToVirtualPath(_notes.Root, _notes.P("a/b.md")));
        Assert.Equal(".", _paths.ToVirtualPath(_notes.Root, _notes.Root));
    }
}
