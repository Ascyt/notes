using notes.Features.Helpers;

namespace notes.Tests.Unit;

public sealed class PathServiceTests : IDisposable
{
    private readonly TempNotes _notes = new();
    private readonly PathService _paths = new();

    public void Dispose() => _notes.Dispose();

    /// <summary>The path as <see cref="PathService.ToPhysicalPath"/> finds it, after checking that <see cref="PathService.FindEntries"/> finds the same.</summary>
    private string? Find(string virtualPath)
    {
        string? physical = _paths.ToPhysicalPath(_notes.Root, virtualPath);
        Assert.Equal(physical, _paths.FindEntries(_notes.Root, [virtualPath])[0]?.FullName);
        return physical;
    }

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
        => Assert.Equal(Path.GetFullPath(_notes.Root), Find(""));

    [Fact]
    public void Finds_existing_entries_case_insensitively_and_returns_their_real_spelling()
    {
        _notes.Files.File("Dir/File.md");

        Assert.Equal(_notes.P("Dir/File.md"), Find("dir/file.MD"));
        Assert.Equal(_notes.P("Dir/File.md"), Find("/Dir//File.md/"));
    }

    [Fact]
    public void Missing_entries_have_no_path()
        => Assert.Null(Find("missing.md"));

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

        Assert.Null(Find(virtualPath));
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
        => Assert.Null(Find(virtualPath));

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

            Assert.Null(Find("link"));
            Assert.Null(Find("link/secret.txt"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Many_paths_are_found_together_each_as_on_its_own()
    {
        _notes.Files.File("Dir/File.md");
        _notes.Files.File("dir2/x[1].md");
        _notes.Files.File("Dir/Sub/deep.md");
        for (int i = 0; i < 20; i++)
            _notes.Files.File($"Dir/img{i}.png");

        string[] paths = ["dir/file.MD", "missing.md", "Dir/img3.png", "", "Dir/../dir2/x[1].md", "dir2/x[1].md", "DIR/sub/DEEP.md",
            "Dir/File.md/nothing", "dir/img19.png", "_/temp", "Dir/img3.png"];
        IReadOnlyList<FileSystemInfo?> found = _paths.FindEntries(_notes.Root, paths);

        Assert.Equal(paths.Select(p => _paths.ToPhysicalPath(_notes.Root, p)), found.Select(entry => entry?.FullName));
        Assert.Equal(_notes.P("Dir/img3.png"), found[2]?.FullName);
        Assert.IsType<DirectoryInfo>(found[3]);
        Assert.IsType<FileInfo>(found[6]);
    }

    [Fact]
    public void A_name_that_looks_like_a_pattern_only_finds_itself()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("* and ? can't be in names on Windows.");

        _notes.Files.File("a*.md");
        _notes.Files.File("ab.md");

        Assert.Equal(_notes.P("a*.md"), Find("a*.md"));
        Assert.Null(Find("a?.md"));
    }

    [Fact]
    public void Where_names_only_differ_in_case_the_exact_spelling_wins()
    {
        _notes.Files.File("a.md", "lower");
        if (File.Exists(_notes.P("A.md")))
            Assert.Skip("The filesystem here doesn't tell names apart by case.");
        _notes.Files.File("A.md", "upper");

        Assert.Equal(_notes.P("A.md"), Find("A.md"));
        Assert.Equal(_notes.P("a.md"), Find("a.md"));
    }

    [Fact]
    public void Virtual_paths_use_slashes()
    {
        Assert.Equal("a/b.md", _paths.ToVirtualPath(_notes.Root, _notes.P("a/b.md")));
        Assert.Equal(".", _paths.ToVirtualPath(_notes.Root, _notes.Root));
    }
}
