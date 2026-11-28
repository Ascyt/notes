using Notes.Features.Configuration;

namespace Notes.Tests.Unit;

/// <summary>
/// The index of the configuration files (<see cref="ConfigIndex"/>, in <see cref="FolderConfigStore"/>): once a location was walked, whether a
/// folder has one is known without the disk, what the app changes counts at once, what is changed by hand once a rescan found it.
/// </summary>
public sealed class ConfigIndexTests : IDisposable
{
    private readonly TempNotes _notes = new();

    public void Dispose() => _notes.Dispose();

    private FolderConfigStore Store => _notes.Store;

    private long ReadsOf(Action asking)
    {
        long before = Store.DiskReads;
        asking();
        return Store.DiskReads - before;
    }

    [Fact]
    public void Once_walked_a_folder_without_a_file_is_not_looked_at_and_one_with_a_file_once()
    {
        _notes.Files.Dir("plain/below");
        _notes.Files.Config("own", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));
        _ = Store.MigrateAll();

        Assert.Equal(0, ReadsOf(() => Assert.Null(Store.Get(_notes.P("plain/below")).Config)));
        Assert.Equal(1, ReadsOf(() => Assert.Contains("g:a", Store.Get(_notes.P("own")).Config!.Perms.RwAllow)));
        Assert.Equal(0, ReadsOf(() => Assert.Contains("g:a", Store.Get(_notes.P("own")).Config!.Perms.RwAllow)));
    }

    [Fact]
    public void What_the_app_writes_counts_at_once()
    {
        _notes.Files.Dir("new");
        _ = Store.MigrateAll();

        Store.WriteText(_notes.P("new"), Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));
        Assert.Contains("g:a", Store.Get(_notes.P("new")).Config!.Perms.RwAllow);
        Store.WriteText(_notes.P("new"), Cfg.Folder(Cfg.Perms(rwAllow: ["g:b"])));
        Assert.Contains("g:b", Store.Get(_notes.P("new")).Config!.Perms.RwAllow);
    }

    [Fact]
    public void What_is_changed_by_hand_counts_once_rescanned()
    {
        _notes.Files.Config("edited", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));
        _notes.Files.Dir("added");
        _ = Store.MigrateAll();
        _ = Store.Get(_notes.P("edited"));

        _notes.Files.Config("edited", Cfg.Folder(Cfg.Perms(rwAllow: ["g:b"])));
        _notes.Files.Config("added", Cfg.Folder(Cfg.Perms(rwAllow: ["g:c"])));
        Assert.Contains("g:a", Store.Get(_notes.P("edited")).Config!.Perms.RwAllow);
        Assert.Null(Store.Get(_notes.P("added")).Config);

        Store.Rescan();
        Assert.Contains("g:b", Store.Get(_notes.P("edited")).Config!.Perms.RwAllow);
        Assert.Contains("g:c", Store.Get(_notes.P("added")).Config!.Perms.RwAllow);

        File.Delete(Path.Combine(_notes.P("added"), ".config.json"));
        Store.Rescan();
        Assert.Null(Store.Get(_notes.P("added")).Config);
    }

    [Fact]
    public void A_moved_tree_takes_its_configurations_along()
    {
        _notes.Files.Config("from/inner", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));
        _ = Store.MigrateAll();
        _ = Store.Get(_notes.P("from/inner"));

        Directory.Move(_notes.P("from"), _notes.P("to"));
        Store.TreeMoved(_notes.P("from"), _notes.P("to"));

        Assert.Contains("g:a", Store.Get(_notes.P("to/inner")).Config!.Perms.RwAllow);
        Assert.Null(Store.Get(_notes.P("from/inner")).Config);
    }

    [Fact]
    public void A_tree_that_came_in_is_walked_and_one_that_went_is_forgotten()
    {
        _notes.Files.Config("gone/inner", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));
        _ = Store.MigrateAll();
        _ = Store.Get(_notes.P("gone/inner"));

        // (As a copy does: written beside the index, then put in place)
        string staging = Path.Combine(Path.GetTempPath(), "notes-unit", Guid.NewGuid().ToString("N"));
        new NotesTree(staging).Config("inner", Cfg.Folder(Cfg.Perms(rwAllow: ["g:b"])));
        Directory.Move(staging, _notes.P("came"));
        Store.TreeChanged(_notes.P("came"));
        Directory.Delete(_notes.P("gone"), recursive: true);
        Store.TreeChanged(_notes.P("gone"));

        Assert.Contains("g:b", Store.Get(_notes.P("came/inner")).Config!.Perms.RwAllow);
        Assert.Null(Store.Get(_notes.P("gone/inner")).Config);
    }

    [Fact]
    public void Before_a_walk_the_disk_is_looked_at_each_time()
    {
        _notes.Files.Config("own", Cfg.Folder(Cfg.Perms(rwAllow: ["g:a"])));
        Assert.Equal(1, ReadsOf(() => Store.Get(_notes.P("own"))));
        _notes.Files.Config("own", Cfg.Folder(Cfg.Perms(rwAllow: ["g:b"])));
        Assert.Contains("g:b", Store.Get(_notes.P("own")).Config!.Perms.RwAllow);
    }
}
