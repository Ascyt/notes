using Microsoft.Extensions.Logging.Abstractions;
using notes.Features.Bridges;
using notes.Features.Helpers;

namespace notes.Tests.Unit;

/// <summary>The bridges (docs/administration/bridges.md): what a configuration of them may be, and how paths go across them.</summary>
public sealed class NotesLocationsTests : IDisposable
{
    private readonly TempNotes _notes = new();
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "notes-unit", Guid.NewGuid().ToString("N") + "-bridges");

    public NotesLocationsTests() => _ = Directory.CreateDirectory(_outside);

    public void Dispose()
    {
        _notes.Dispose();
        TestDirectories.Delete(_outside);
    }

    private string Outside(string name) => Directory.CreateDirectory(Path.Combine(_outside, name)).FullName;

    private NotesLocations With(params (string Virtual, string Physical)[] bridges)
        => new(new Config
        {
            Dir = _notes.Root,
            Version = _notes.Config.Version,
            Auth = _notes.Config.Auth,
            Bridges = [.. bridges.Select(bridge => new BridgeConfig { Virtual = bridge.Virtual, Physical = bridge.Physical })]
        });

    /// <summary>The locations, checked on disk as the app does when it starts.</summary>
    private NotesLocations Checked(params (string Virtual, string Physical)[] bridges)
    {
        NotesLocations locations = With(bridges);
        locations.CheckOnDisk(new PathService(locations).ToPhysicalPath, NullLogger.Instance);
        return locations;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("/_/x")]
    [InlineData("/a/../b")]
    [InlineData("/.config.json")]
    public void A_virtual_path_that_is_not_one_of_the_notes_is_refused(string virtualPath)
        => Assert.Throws<InvalidOperationException>(() => With((virtualPath, Outside("x"))));

    [Fact]
    public void A_physical_path_that_is_not_a_full_one_is_refused()
        => Assert.Throws<InvalidOperationException>(() => With(("/hdd", "relative\\dir")));

    [Fact]
    public void Two_bridges_at_the_same_virtual_path_are_refused()
        => Assert.Throws<InvalidOperationException>(() => With(("/hdd", Outside("a")), ("/HDD", Outside("b"))));

    [Fact]
    public void Physical_directories_that_overlap_are_refused()
    {
        string a = Outside("a");
        // Inside the notes, inside another bridge, around another bridge, the same as another bridge
        Assert.Throws<InvalidOperationException>(() => With(("/hdd", _notes.Files.Dir("inside"))));
        Assert.Throws<InvalidOperationException>(() => With(("/one", a), ("/two", Path.Combine(a, "inner"))));
        Assert.Throws<InvalidOperationException>(() => With(("/one", Path.Combine(a, "inner")), ("/two", a)));
        Assert.Throws<InvalidOperationException>(() => With(("/one", a), ("/two", a)));
        Assert.Throws<InvalidOperationException>(() => With(("/hdd", Path.GetDirectoryName(_notes.Root)!)));
    }

    [Fact]
    public void A_physical_directory_that_is_missing_stops_the_app()
        => Assert.Throws<InvalidOperationException>(() => Checked(("/hdd", Path.Combine(_outside, "missing"))));

    [Fact]
    public void A_bridge_whose_directory_above_is_missing_stops_the_app()
        => Assert.Throws<InvalidOperationException>(() => Checked(("/missing/hdd", Outside("a"))));

    [Fact]
    public void A_bridge_inside_another_is_mounted_in_the_physical_directory_of_that_one()
    {
        string hdd = Outside("hdd");
        string archive = Outside("archive");
        _ = Directory.CreateDirectory(Path.Combine(hdd, "sub"));

        NotesLocations locations = Checked(("/hdd/sub/archive", archive), ("/hdd", hdd));

        Assert.Equal(Path.Combine(hdd, "sub", "archive"), locations.BridgeAt("hdd/sub/archive")!.MountPath);
        Assert.Equal(Path.Combine(hdd, "sub"), locations.ParentOf(archive));
        Assert.Equal(_notes.Root, locations.ParentOf(hdd));
        Assert.Null(locations.ParentOf(_notes.Root));
    }

    [Fact]
    public void Paths_go_across_the_bridges_both_ways()
    {
        string hdd = Outside("hdd");
        _ = Directory.CreateDirectory(Path.Combine(hdd, "dir"));
        NotesLocations locations = Checked(("/hdd", hdd));
        PathService paths = new(locations);

        Assert.Equal(Path.Combine(hdd, "dir"), paths.ToPhysicalPath("/hdd/dir"));
        Assert.Equal("hdd/dir/new.md", locations.ToVirtualPath(Path.Combine(hdd, "dir", "new.md")));
        Assert.Equal("hdd", locations.NameOf(hdd));
        Assert.Equal("hdd", locations.LocationOfVirtual("HDD/dir/x").VirtualPath);
        Assert.True(locations.LocationOfVirtual("hdd2").IsRoot);
        Assert.True(locations.IsSameOrInside(Path.Combine(hdd, "dir"), _notes.Root));
        Assert.True(locations.HoldsBridge(_notes.Root));
        Assert.False(locations.HoldsBridge(Path.Combine(hdd, "dir")));
    }

    [Fact]
    public void A_bridge_hides_what_is_on_disk_where_it_is_mounted()
    {
        string hdd = Outside("hdd");
        _ = _notes.Files.File("hdd/hidden.md", "hidden");
        NotesLocations locations = Checked(("/hdd", hdd));

        Assert.True(locations.IsHiddenByBridge(_notes.P("hdd/hidden.md")));
        Assert.False(locations.IsInNotes(_notes.P("hdd/hidden.md")));
        Assert.False(locations.IsFree(_notes.P("hdd")));
        Assert.Null(new PathService(locations).ToPhysicalPath("/hdd/hidden.md"));
        Assert.Equal([("hdd", hdd)], locations.ListDirectory(new DirectoryInfo(_notes.Root), new EnumerationOptions())
            .Where(child => child.Entry is DirectoryInfo).Select(child => (child.Name, child.Entry.FullName)));
    }

    [Fact]
    public void Paths_outside_of_all_locations_are_not_in_the_notes()
    {
        NotesLocations locations = Checked(("/hdd", Outside("hdd")));

        Assert.False(locations.IsInNotes(Outside("elsewhere")));
        Assert.Null(locations.LocationOf(Outside("elsewhere")));
    }
}
