using System.Security.Claims;
using notes.Features.Auth;

namespace notes.Tests.Unit;

/// <summary>Known, openable and hidden entries, and walking trees (security specification, "Known, openable, hidden").</summary>
public sealed class AccessTreeTests : IDisposable
{
    private readonly TempNotes _notes = new();

    public AccessTreeTests() => BaseTree.Create(_notes.Files);

    public void Dispose() => _notes.Dispose();

    private static ClaimsPrincipal Alice => Users.Alice.ToPrincipal();

    private List<string> WalkAs(TestUser user, string directory)
        => _notes.Tree.Walk(user.ToPrincipal(), new DirectoryInfo(_notes.P(directory)))
            .Select(walked => _notes.Tree.Display(walked.Entry.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void The_root_is_always_known_but_only_openable_with_read()
    {
        // Nobody is logged in, so the a of the base tree gives them read: take it away
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));
        Assert.True(_notes.Tree.IsKnown(Users.Nobody.ToPrincipal(), _notes.Root));
        Assert.False(_notes.Tree.CanOpen(Users.Nobody.ToPrincipal(), _notes.Root));
        Assert.True(_notes.Tree.CanOpen(Alice, _notes.Root));
    }

    [Theory]
    [InlineData("readme.md", true, true)]
    [InlineData("docs/nested/b.md", true, true)]
    [InlineData("vault", true, false)]
    [InlineData("mixed/locked.md", true, false)]
    [InlineData("mixed/closed", true, false)]
    [InlineData("broken", true, false)]
    [InlineData("vault/secret.md", false, false)]
    [InlineData("vault/inner/deep.md", false, false)]
    [InlineData("vault/does-not-exist.md", false, false)]
    [InlineData("mixed/closed/hidden.md", false, false)]
    [InlineData("broken/x.md", false, false)]
    public void Known_and_openable_for_alice(string path, bool known, bool openable)
    {
        Assert.Equal(known, _notes.Tree.IsKnown(Alice, _notes.P(path)));
        Assert.Equal(openable, _notes.Tree.CanOpen(Alice, _notes.P(path)));
    }

    [Theory]
    [InlineData("readme.md", true)]
    [InlineData("mixed/open.md", true)]
    [InlineData("vault", false)]
    [InlineData("team", false)]
    [InlineData("mixed/locked.md", false)]
    [InlineData("mixed/closed", false)]
    [InlineData("broken", false)]
    public void For_anonymous_users_known_is_openable(string path, bool openable)
    {
        ClaimsPrincipal anonymous = Users.Anonymous.ToPrincipal();
        Assert.Equal(openable, _notes.Tree.IsKnown(anonymous, _notes.P(path)));
        Assert.Equal(openable, _notes.Tree.CanOpen(anonymous, _notes.P(path)));
    }

    [Fact]
    public void For_anonymous_users_the_root_is_known_even_if_they_may_not_read_it()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));
        Assert.True(_notes.Tree.IsKnown(Users.Anonymous.ToPrincipal(), _notes.Root));
    }

    [Fact]
    public void A_walk_leaves_out_unreadable_entries_for_anonymous_users()
    {
        List<string> walked = WalkAs(Users.Anonymous, "mixed");

        Assert.Equal(["mixed/keep.md", "mixed/open.md", "mixed/view.md"], walked);
    }

    [Fact]
    public void Nothing_below_an_unreadable_root_is_known()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));
        Assert.False(_notes.Tree.IsKnown(Users.Nobody.ToPrincipal(), _notes.P("readme.md")));
        Assert.False(_notes.Tree.IsKnown(Users.Nobody.ToPrincipal(), _notes.P("docs")));
    }

    [Fact]
    public void Paths_outside_the_notes_are_never_known()
    {
        Assert.False(_notes.Tree.IsKnown(Users.Admin.ToPrincipal(), Path.GetDirectoryName(_notes.Root)!));
        Assert.False(_notes.Tree.CanOpen(Users.Admin.ToPrincipal(), Path.Combine(_notes.Root, "..", "x")));
    }

    [Fact]
    public void A_walk_lists_unreadable_entries_but_does_not_enter_them_and_leaves_out_configurations()
    {
        List<string> walked = WalkAs(Users.Alice, "mixed");

        Assert.Equal(["mixed/closed", "mixed/keep.md", "mixed/locked.md", "mixed/open.md", "mixed/view.md"], walked);
    }

    [Fact]
    public void A_walk_of_the_root_never_enters_the_reserved_folder_or_what_alice_may_not_read()
    {
        _notes.Files.File("_/temp/user/x.part");

        List<string> walked = WalkAs(Users.Alice, "");

        Assert.DoesNotContain(walked, path => path == "_" || path.StartsWith("_/", StringComparison.Ordinal));
        Assert.DoesNotContain(walked, path => path.EndsWith(".config.json", StringComparison.Ordinal));
        foreach (string hidden in BaseTree.HiddenFromAlice)
            Assert.DoesNotContain(hidden, walked);
        Assert.Contains("vault", walked);
    }

    [Fact]
    public void Admins_walk_everything_including_the_configurations()
    {
        List<string> walked = WalkAs(Users.Admin, "mixed");

        Assert.Contains("mixed/.config.json", walked);
        Assert.Contains("mixed/closed/.config.json", walked);
        Assert.Contains("mixed/closed/hidden.md", walked);
    }

    [Fact]
    public void Inspect_says_why_a_tree_can_not_be_deleted_without_naming_hidden_entries()
    {
        // mixed contains closed (unreadable): fails because of closed itself, never because of what is in it
        TreeAccess mixed = _notes.Tree.Inspect(Alice, _notes.P("mixed"));
        Assert.False(mixed.CanDelete);
        Assert.DoesNotContain("hidden.md", mixed.DeleteDeniedReason);

        Assert.True(_notes.Tree.Inspect(Alice, _notes.P("docs")).CanDelete);
        Assert.False(_notes.Tree.Inspect(Alice, _notes.P("mixed/keep.md")).CanDelete);
        Assert.False(_notes.Tree.Inspect(Alice, _notes.P("drop/existing.md")).CanDelete);
        Assert.Equal("Insufficient permissions to delete or move 'drop'.", _notes.Tree.Inspect(Alice, _notes.P("drop")).DeleteDeniedReason);
    }

    [Fact]
    public void Hints_for_creating_and_renaming()
    {
        Assert.Null(_notes.Tree.CreateDeniedReason(Alice, _notes.P("docs")));
        Assert.Null(_notes.Tree.CreateDeniedReason(Alice, _notes.P("drop")));
        Assert.NotNull(_notes.Tree.CreateDeniedReason(Alice, _notes.P("shelf")));
        Assert.NotNull(_notes.Tree.CreateDeniedReason(Users.Bob.ToPrincipal(), _notes.P("docs")));

        string item = _notes.P("shelf/item.md");
        Assert.NotNull(_notes.Tree.RenameDeniedReason(Alice, item, _notes.Tree.Inspect(Alice, item)));
        string a = _notes.P("docs/a.md");
        Assert.Null(_notes.Tree.RenameDeniedReason(Alice, a, _notes.Tree.Inspect(Alice, a)));
    }
}
