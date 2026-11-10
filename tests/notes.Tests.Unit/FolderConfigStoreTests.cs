using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using notes.Features.Permissions;
using notes.Features.Permissions.Migrations;
using Semver;

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

    // ---- Versions: a file of an older version is read as the current one

    /// <summary>A step that renames what 1.0.0 called "inheritance" in a perms block to "inherit", as the current version does.</summary>
    private sealed class RenameInheritance : IConfigMigration
    {
        public SemVersion Version => new(1, 1, 0);
        public string Description => "perms.inheritance is perms.inherit now";

        public void Apply(JsonObject root)
        {
            JsonObject perms = root["perms"]?.AsObject() ?? throw new InvalidOperationException("no perms");
            JsonNode value = perms["inheritance"] ?? throw new InvalidOperationException("no inheritance");
            _ = perms.Remove("inheritance");
            perms["inherit"] = value;
        }
    }

    private const string OldFolder = """{ "version": "1.0.0", "perms": { "inheritance": false }, "files": [] }""";

    private FolderConfigStore StoreOf(string version, params IConfigMigration[] steps)
        => new(_notes.Config, new ConfigMigrator(ConfigVersions.Parse(version), steps, NullLogger<ConfigMigrator>.Instance), NullLogger<FolderConfigStore>.Instance);

    [Fact]
    public void A_file_without_a_version_is_read_as_the_current_one()
    {
        _notes.Files.Config("", """{ "perms": { "inherit": false }, "files": [] }""");

        FolderConfigState state = _notes.Store.Get(_notes.Root);

        Assert.False(state.IsInvalid);
        Assert.False(state.Config!.Perms.Inherit);
        Assert.Contains("\"version\": \"1.0.0\"", _notes.Store.ReadText(_notes.Root));
    }

    [Fact]
    public void A_file_of_an_older_version_is_migrated_when_it_is_read_but_not_written()
    {
        _notes.Files.Config("", OldFolder);
        FolderConfigStore store = StoreOf("1.1.0", new RenameInheritance());

        FolderConfigState state = store.Get(_notes.Root);

        Assert.False(state.IsInvalid, string.Join(", ", state.Errors));
        Assert.False(state.Config!.Perms.Inherit);
        Assert.Contains("\"inherit\": false", store.ReadText(_notes.Root));
        Assert.Equal(OldFolder, File.ReadAllText(Path.Combine(_notes.Root, ".config.json")));
    }

    [Fact]
    public void A_file_of_a_newer_version_is_invalid_so_only_admins_have_access()
    {
        _notes.Files.Config("", """{ "version": "1.1.0", "perms": { "inherit": true }, "files": [] }""");

        FolderConfigState state = _notes.Store.Get(_notes.Root);

        Assert.True(state.IsInvalid);
        Assert.Contains("newer", Assert.Single(state.Errors));
    }

    [Fact]
    public void A_migration_that_fails_makes_the_file_invalid_and_leaves_it_alone()
    {
        // Nothing to rename in this one
        _notes.Files.Config("", """{ "version": "1.0.0", "perms": { "inherit": true }, "files": [] }""");
        FolderConfigStore store = StoreOf("1.1.0", new RenameInheritance());

        Assert.True(store.Get(_notes.Root).IsInvalid);
        Assert.Contains("\"inherit\": true", File.ReadAllText(Path.Combine(_notes.Root, ".config.json")));
    }

    [Fact]
    public void Entries_are_kept_in_step_on_what_is_migrated()
    {
        _notes.Files.Config("dir", OldFolder);
        string dir = _notes.P("dir");
        FolderConfigStore store = StoreOf("1.1.0", new RenameInheritance());

        store.FileAdded(dir, "a.md");

        string written = File.ReadAllText(Path.Combine(dir, ".config.json"));
        Assert.Contains("\"version\": \"1.1.0\"", written);
        Assert.Contains("\"inherit\": false", written);
        Assert.DoesNotContain("inheritance", written);
        Assert.Equal(["a.md"], ConfigText.FileNames(written));
    }

    [Fact]
    public void Migrating_everything_writes_each_old_file_and_nothing_else()
    {
        _notes.Files.Config("", OldFolder);
        _notes.Files.Config("a/b", OldFolder);
        _notes.Files.Config("broken", "{ broken");
        string current = """{ "version": "1.1.0", "perms": { "inherit": true }, "files": [] }""";
        _notes.Files.Config("current", current);
        // What is kept for the app itself is not part of the notes
        _notes.Files.Config("_/temp", OldFolder);
        FolderConfigStore store = StoreOf("1.1.0", new RenameInheritance());

        int migrated = store.MigrateAll();

        Assert.Equal(2, migrated);
        foreach (string path in new[] { "", "a/b" })
        {
            string text = File.ReadAllText(Path.Combine(_notes.P(path), ".config.json"));
            Assert.Contains("\"version\": \"1.1.0\"", text);
            Assert.Contains("\"inherit\": false", text);
        }

        Assert.Equal("{ broken", File.ReadAllText(Path.Combine(_notes.P("broken"), ".config.json")));
        Assert.Equal(current, File.ReadAllText(Path.Combine(_notes.P("current"), ".config.json")));
        Assert.Equal(OldFolder, File.ReadAllText(Path.Combine(_notes.P("_/temp"), ".config.json")));
        Assert.Equal(0, store.MigrateAll());
    }

    [Fact]
    public void The_names_of_the_files_leave_out_the_configuration_and_directories()
    {
        _notes.Files.Config("dir", Cfg.Folder(Cfg.Perms()));
        _ = _notes.Files.File("dir/a.md");
        _ = _notes.Files.Dir("dir/sub");

        Assert.Equal(["a.md"], FolderConfigStore.FileNamesOf(_notes.P("dir")));
    }
}
