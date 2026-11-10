using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using notes.Features.Components.Selection;
using notes.Features.Helpers;

namespace notes.Tests.Unit;

public sealed class SelectionServiceTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly MemoryCache _cache;
    private readonly SelectionService _selections;

    public SelectionServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { Clock = new TimeProviderClock(_time) });
        _selections = new SelectionService(_cache, _time);
    }

    public void Dispose() => _cache.Dispose();

    private static SelectionEntry File(string path, bool canDelete = true) => new(path, false, canDelete);
    private static SelectionEntry Directory(string path, bool canDelete = true) => new(path, true, canDelete);

    [Fact]
    public void Selecting_and_unselecting()
    {
        SelectionSnapshot empty = _selections.Get("u");
        SelectionSnapshot added = _selections.Add("u", [File("b.md"), Directory("a"), File("B.MD")])!;
        SelectionSnapshot removed = _selections.Remove("u", ["A"])!;

        Assert.Empty(empty.Paths);
        Assert.Equal(["a", "b.md"], added.Paths);
        Assert.Equal(1, added.DirectoryCount);
        Assert.True(added.Version > empty.Version);
        Assert.Equal(["b.md"], removed.Paths);
        Assert.True(removed.Version > added.Version);
    }

    [Fact]
    public void A_change_that_changes_nothing_keeps_the_version()
    {
        long version = _selections.Add("u", [File("a.md")])!.Version;

        Assert.Equal(version, _selections.Add("u", [File("a.md")])!.Version);
        Assert.Equal(version, _selections.Remove("u", ["x.md"])!.Version);
    }

    [Fact]
    public void Users_have_their_own_selections()
    {
        _ = _selections.Add("u", [File("a.md")]);

        Assert.Empty(_selections.Get("v").Paths);
    }

    [Fact]
    public void Can_delete_only_if_every_entry_can_be_deleted()
    {
        Assert.True(_selections.Add("u", [File("a.md")])!.CanDelete);
        Assert.False(_selections.Add("u", [File("b.md", canDelete: false)])!.CanDelete);
        Assert.True(_selections.Remove("u", ["b.md"])!.CanDelete);
    }

    [Fact]
    public void While_a_transfer_runs_the_selection_can_not_change_and_no_second_one_starts()
    {
        SelectionSnapshot selection = _selections.Add("u", [File("a.md")])!;

        TransferBegin begin = _selections.TryBeginTransfer("u", selection.Version);
        Assert.Equal(TransferBeginOutcome.Started, begin.Outcome);
        Assert.Equal(["a.md"], begin.Paths);
        Assert.True(_selections.Get("u").Busy);
        Assert.Null(_selections.Add("u", [File("b.md")]));
        Assert.Null(_selections.Remove("u", ["a.md"]));
        Assert.Null(_selections.Clear("u"));
        Assert.Equal(TransferBeginOutcome.Busy, _selections.TryBeginTransfer("u", _selections.Get("u").Version).Outcome);

        _selections.EndTransfer("u", clear: true);
        Assert.False(_selections.Get("u").Busy);
        Assert.Empty(_selections.Get("u").Paths);
    }

    [Fact]
    public void A_failed_transfer_keeps_the_selection()
    {
        SelectionSnapshot selection = _selections.Add("u", [File("a.md")])!;
        _ = _selections.TryBeginTransfer("u", selection.Version);

        _selections.EndTransfer("u", clear: false);

        Assert.Equal(["a.md"], _selections.Get("u").Paths);
    }

    [Fact]
    public void A_transfer_of_an_outdated_or_empty_selection_does_not_start()
    {
        SelectionSnapshot old = _selections.Add("u", [File("a.md")])!;
        _ = _selections.Add("u", [File("b.md")]);

        Assert.Equal(TransferBeginOutcome.Changed, _selections.TryBeginTransfer("u", old.Version).Outcome);
        SelectionSnapshot cleared = _selections.Clear("u")!;
        Assert.Equal(TransferBeginOutcome.Empty, _selections.TryBeginTransfer("u", cleared.Version).Outcome);
    }

    // ---- The lifetime of a selection

    [Fact]
    [Trait("Category", "Timing")]
    public void A_selection_is_forgotten_after_30_minutes_without_use()
    {
        _ = _selections.Add("u", [File("a.md")]);

        _time.Advance(TimeSpan.FromMinutes(31));

        Assert.Empty(_selections.Get("u").Paths);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void Every_use_renews_the_lifetime()
    {
        _ = _selections.Add("u", [File("a.md")]);

        for (int i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(["a.md"], _selections.Get("u").Paths);
        }
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void A_transfer_of_a_selection_that_was_forgotten_does_not_start()
    {
        SelectionSnapshot selection = _selections.Add("u", [File("a.md")])!;

        _time.Advance(TimeSpan.FromMinutes(31));

        Assert.NotEqual(TransferBeginOutcome.Started, _selections.TryBeginTransfer("u", selection.Version).Outcome);
    }

    /// <summary>
    /// A copy or move of many files can take longer than the lifetime of the selection, and nothing touches the selection while it runs.
    /// The selection must stay busy until the transfer ends: otherwise another tab can select and start a second transfer meanwhile, and the
    /// end of the first one clears what was selected in between.
    /// </summary>
    [Fact]
    [Trait("Category", "Timing")]
    public void A_selection_that_is_being_transferred_is_not_forgotten()
    {
        SelectionSnapshot selection = _selections.Add("u", [File("a.md")])!;
        Assert.Equal(TransferBeginOutcome.Started, _selections.TryBeginTransfer("u", selection.Version).Outcome);

        _time.Advance(TimeSpan.FromMinutes(31));

        Assert.True(_selections.Get("u").Busy, "The selection is no longer busy while its transfer still runs.");
        Assert.Null(_selections.Add("u", [File("b.md")]));

        // Once it is done, its lifetime starts again
        _selections.EndTransfer("u", clear: false);
        _time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(["a.md"], _selections.Get("u").Paths);
        _time.Advance(TimeSpan.FromMinutes(31));
        Assert.Empty(_selections.Get("u").Paths);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void A_forgotten_selection_is_followed_by_one_that_looks_newer()
    {
        SelectionSnapshot old = _selections.Add("u", [File("a.md")])!;
        for (int i = 0; i < 100; i++)
            old = _selections.Add("u", [File("f" + i)])!;

        _time.Advance(TimeSpan.FromMinutes(31));

        Assert.True(_selections.Get("u").Version > old.Version);
    }
}
