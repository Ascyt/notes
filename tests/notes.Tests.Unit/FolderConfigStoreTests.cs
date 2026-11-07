using notes.Features.Permissions;

namespace notes.Tests.Unit;

public sealed class FolderConfigStoreTests : IDisposable
{
    private readonly TempNotes _notes = new();

    public void Dispose() => _notes.Dispose();

    [Fact]
    public void A_folder_without_a_file_says_nothing()
    {
        FolderConfigState state = _notes.Store.Get(_notes.Root);

        Assert.Null(state.Config);
        Assert.False(state.IsInvalid);
    }

    [Fact]
    public void An_unreadable_file_is_invalid()
    {
        _notes.Files.Config("", "{");

        Assert.True(_notes.Store.Get(_notes.Root).IsInvalid);
    }

    [Fact]
    public void A_changed_file_is_read_again()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));
        Assert.Contains("g:a", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);

        // Same length, so only the time tells
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(rwAllow: ["g:b"])));
        Assert.Contains("g:b", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);
    }

    [Fact]
    public void Within_a_read_scope_a_folder_is_read_once_and_afterwards_again()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));

        using (ConfigReadScope.Begin())
        {
            Assert.Contains("g:a", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);

            // The scope keeps what it saw, a file that appears or changes meanwhile is not looked at
            _notes.Files.Config("", Cfg.Folder(Cfg.Perms(rwAllow: ["g:b"])));
            _notes.Files.Config("sub", Cfg.Folder(Cfg.Perms()));
            Assert.Contains("g:a", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);
            Assert.NotNull(_notes.Store.Get(_notes.P("sub")).Config);
        }

        Assert.Contains("g:b", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);
    }

    [Fact]
    public void Read_scopes_nest_and_the_outer_one_decides_when_it_ends()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));

        using (ConfigReadScope.Begin())
        {
            using (ConfigReadScope.Begin())
                Assert.Contains("g:a", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);

            _notes.Files.Config("", Cfg.Folder(Cfg.Perms(rwAllow: ["g:b"])));
            Assert.Contains("g:a", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);
        }

        Assert.Contains("g:b", _notes.Store.Get(_notes.Root).Config!.Perms.RwAllow);
    }

    [Fact]
    public void Writing_replaces_the_file_and_leaves_no_temp_file_in_the_folder()
    {
        _notes.Store.WriteText(_notes.Root, Cfg.Folder(Cfg.Perms()));

        Assert.True(_notes.Store.Get(_notes.Root).Config is not null);
        Assert.Equal([".config.json"], Directory.EnumerateFiles(_notes.Root).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_notes.Root, "_", "temp", "config")));
    }

    [Fact]
    public void Entries_follow_the_files_of_a_folder_with_a_configuration()
    {
        _notes.Files.Config("dir", Cfg.Folder(Cfg.Perms()));
        string dir = _notes.P("dir");

        _notes.Store.FileAdded(dir, "a.md");
        _notes.Store.FileAdded(dir, "b.md");
        _notes.Store.FileRenamed(dir, "a.md", "c.md");
        _notes.Store.FileRemoved(dir, "b.md");

        Assert.Equal(["c.md"], ConfigText.FileNames(_notes.Store.ReadText(dir)!));
        Assert.False(_notes.Store.Get(dir).IsInvalid);
    }

    [Fact]
    public void A_folder_without_a_configuration_stays_without_one()
    {
        string dir = _notes.Files.Dir("dir");

        _notes.Store.FileAdded(dir, "a.md");

        Assert.Null(_notes.Store.ReadText(dir));
    }

    [Fact]
    public void An_invalid_configuration_is_left_as_it_is()
    {
        _notes.Files.Config("dir", "{ broken");
        string dir = _notes.P("dir");

        _notes.Store.FileAdded(dir, "a.md");

        Assert.Equal("{ broken", _notes.Store.ReadText(dir));
    }

    [Fact]
    public void The_names_of_the_files_leave_out_the_configuration_and_directories()
    {
        _notes.Files.Config("dir", Cfg.Folder(Cfg.Perms()));
        _notes.Files.File("dir/a.md");
        _notes.Files.Dir("dir/sub");

        Assert.Equal(["a.md"], FolderConfigStore.FileNamesOf(_notes.P("dir")));
    }
}
