using notes.Features.Auth;

namespace notes.Tests.Unit;

/// <summary>How the configuration files are resolved into permissions (docs/administration/permissions.md, "What they mean").</summary>
public sealed class EntryAccessTests : IDisposable
{
    private readonly TempNotes _notes = new();

    public void Dispose() => _notes.Dispose();

    private static readonly EntryAccessInfo Everything = new(ReadAccess.ReadAndWrite, CreateAccess.CreateAndDelete);
    private static readonly EntryAccessInfo ReadOnly = new(ReadAccess.Read, CreateAccess.None);

    [Fact]
    public void Without_any_configuration_only_admins_have_access()
    {
        _ = _notes.Files.File("a/b.md");

        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, ""));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "a/b.md"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Anonymous, "a"));
        Assert.Equal(Everything, _notes.Get(Users.Admin, "a/b.md"));
        Assert.Equal(Everything, _notes.Get(Users.Admin, "does/not/exist"));
    }

    [Fact]
    public void Admins_may_do_everything_even_where_the_configuration_denies_them_or_is_invalid()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwDeny: ["r:admin", "u:admin"], cdDeny: ["r:admin"])));
        _notes.Files.Config("broken", "nope");

        Assert.Equal(Everything, _notes.Get(Users.Admin, ""));
        Assert.Equal(Everything, _notes.Get(Users.Admin, "broken/x.md"));
        Assert.True(_notes.Access.CanManagePermissions(Users.Admin.ToPrincipal()));
        Assert.False(_notes.Access.CanManagePermissions(Users.Alice.ToPrincipal()));
    }

    [Fact]
    public void A_folder_gives_its_permissions_to_everything_in_it()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwReadOnly: ["g:team"])));
        _ = _notes.Files.File("a/b/c.md");

        Assert.Equal(ReadOnly, _notes.Get(Users.Alice, "a/b/c.md"));
        Assert.Equal(ReadOnly, _notes.Get(Users.Alice, "a/b"));
        Assert.Equal(ReadOnly, _notes.Get(Users.Alice, "a/new.md"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Bob, "a/b/c.md"));
    }

    [Fact]
    public void The_nearest_layer_that_mentions_the_user_decides_and_can_take_away_or_give()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"], rwReadOnly: ["g:guests"])));
        _notes.Files.Config("restricted", Cfg.Folder(Cfg.Perms(rwDeny: ["u:alice"])));
        _notes.Files.Config("guestbook", Cfg.Folder(Cfg.Perms(cdCreateOnly: ["g:guests"])));

        // Taken away from alice only; the rest of the team inherits
        Assert.Equal(new EntryAccessInfo(ReadAccess.None, CreateAccess.CreateAndDelete), _notes.Get(Users.Alice, "restricted/x.md"));
        Assert.Equal(Everything, _notes.Get(Users.Carol, "restricted/x.md"));
        // Given: guests may add to the guestbook; reading is still decided above
        Assert.Equal(new EntryAccessInfo(ReadAccess.Read, CreateAccess.Create), _notes.Get(Users.Bob, "guestbook/entry.md"));
    }

    [Fact]
    public void Inherit_false_ends_the_search()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"])));
        _notes.Files.Config("private", Cfg.Folder(Cfg.Perms(inherit: false, rwReadOnly: ["u:carol"])));
        _notes.Files.Config("private/open", Cfg.Folder(Cfg.Perms()));

        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "private"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "private/open/x.md"));
        Assert.Equal(ReadOnly, _notes.Get(Users.Carol, "private/open/x.md"));
    }

    [Fact]
    public void Read_and_create_are_decided_independently_by_different_layers()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwReadOnly: ["g:team"])));
        _notes.Files.Config("inbox", Cfg.Folder(Cfg.Perms(cdCreateOnly: ["g:team"])));

        Assert.Equal(new EntryAccessInfo(ReadAccess.Read, CreateAccess.Create), _notes.Get(Users.Alice, "inbox/new.md"));
    }

    [Fact]
    public void A_file_entry_comes_first_and_applies_by_name_whether_or_not_the_file_exists()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"]),
            ("secret.md", Cfg.Perms(inherit: false, rwAllow: ["u:carol"])),
            ("readonly.md", Cfg.Perms(rwReadOnly: ["u:alice"]))));

        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "secret.md"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "SECRET.MD"));
        Assert.Equal(new EntryAccessInfo(ReadAccess.ReadAndWrite, CreateAccess.None), _notes.Get(Users.Carol, "secret.md"));

        _ = _notes.Files.File("secret.md");
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "secret.md"));

        Assert.Equal(new EntryAccessInfo(ReadAccess.Read, CreateAccess.CreateAndDelete), _notes.Get(Users.Alice, "readonly.md"));
    }

    [Fact]
    public void A_directory_is_its_own_folder_and_its_name_in_the_folder_above_means_nothing()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"]), ("dir", Cfg.Perms(inherit: false))));
        _ = _notes.Files.Dir("dir");

        Assert.True(_notes.Get(Users.Alice, "dir").CanRead);
    }

    [Fact]
    public void An_invalid_configuration_closes_everything_under_it_to_everyone_but_admins()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["a"])));
        _notes.Files.Config("dir", """{ "perms": { "inherit": true, "rwAllow": ["a"] }, "files": [] }""");
        _ = _notes.Files.File("dir/sub/x.md");

        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "dir"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "dir/sub/x.md"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Anonymous, "dir/sub/x.md"));
        Assert.True(_notes.Get(Users.Alice, "other").CanWrite);
    }

    [Fact]
    public void Knowing_whether_a_path_is_a_directory_does_not_change_the_answer()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["l"]), ("f.md", Cfg.Perms(inherit: false, rwReadOnly: ["g:team"]))));
        _notes.Files.Config("closed", Cfg.Folder(Cfg.Perms(inherit: false)));
        _ = _notes.Files.File("f.md");
        _ = _notes.Files.File("g.md");
        _ = _notes.Files.Dir("open");
        _ = _notes.Files.Dir("closed");

        foreach (TestUser user in new[] { Users.Alice, Users.Nobody, Users.Anonymous, Users.Admin })
        {
            System.Security.Claims.ClaimsPrincipal principal = user.ToPrincipal();
            foreach (string file in new[] { "f.md", "g.md" })
                Assert.Equal(_notes.Access.GetAccess(principal, _notes.P(file)), _notes.Access.GetAccess(principal, _notes.P(file), isDirectory: false));
            foreach (string directory in new[] { "open", "closed", "" })
                Assert.Equal(_notes.Access.GetAccess(principal, _notes.P(directory)), _notes.Access.GetAccess(principal, _notes.P(directory), isDirectory: true));
        }

        Assert.Equal(ReadOnly, _notes.Access.GetAccess(Users.Alice.ToPrincipal(), _notes.P("f.md"), isDirectory: false));
        Assert.Equal(EntryAccessInfo.None, _notes.Access.GetAccess(Users.Alice.ToPrincipal(), _notes.P("closed"), isDirectory: true));
    }

    [Fact]
    public void An_invalid_configuration_below_does_not_close_what_is_above()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));
        _notes.Files.Config("a/b", "{");

        Assert.True(_notes.Get(Users.Alice, "a").CanRead);
        Assert.False(_notes.Get(Users.Alice, "a/b").CanRead);
    }

    [Fact]
    public void A_configuration_that_is_fixed_takes_effect()
    {
        _notes.Files.Config("", "{");
        Assert.False(_notes.Get(Users.Alice, "").CanRead);

        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwReadOnly: ["g:team"])));
        Assert.True(_notes.Get(Users.Alice, "").CanRead);
    }

    [Fact]
    public void The_configuration_files_themselves_are_for_admins_only()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"]), (".config.json", Cfg.Perms(rwAllow: ["g:team"]))));
        _notes.Files.Config("dir", Cfg.Folder(Cfg.Perms()));

        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, ".config.json"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "dir/.config.json"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "dir/.CONFIG.JSON"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Alice, "new/.config.json"));
        Assert.Equal(Everything, _notes.Get(Users.Admin, "dir/.config.json"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../sibling")]
    [InlineData("../sibling/x.md")]
    public void Paths_outside_the_notes_give_nothing(string relative)
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"])));

        string outside = Path.GetFullPath(Path.Combine(_notes.Root, relative));
        Assert.Equal(EntryAccessInfo.None, _notes.Access.GetAccess(Users.Alice.ToPrincipal(), outside));
    }

    [Fact]
    public void A_sibling_directory_whose_name_starts_like_the_root_is_outside()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"])));

        Assert.Equal(EntryAccessInfo.None, _notes.Access.GetAccess(Users.Alice.ToPrincipal(), _notes.Root + "-evil" + Path.DirectorySeparatorChar + "x.md"));
        Assert.False(_notes.Tree.IsKnown(Users.Alice.ToPrincipal(), _notes.Root + "-evil"));
    }

    [Fact]
    public void A_path_answers_the_same_whether_or_not_a_file_is_there()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwReadOnly: ["g:team"], cdCreateOnly: ["g:team"]), ("f.md", Cfg.Perms(cdDeny: ["g:team"]))));

        EntryAccessInfo before = _notes.Get(Users.Alice, "f.md");
        EntryAccessInfo otherBefore = _notes.Get(Users.Alice, "g.md");
        _ = _notes.Files.File("f.md");
        _ = _notes.Files.File("g.md");

        Assert.Equal(before, _notes.Get(Users.Alice, "f.md"));
        Assert.Equal(otherBefore, _notes.Get(Users.Alice, "g.md"));
    }

    [Fact]
    public void Everyone_who_is_logged_in_gets_at_least_what_is_given_to_a()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwReadOnly: ["a"], rwAllow: ["g:team"], cdAllow: ["g:team"])));
        _notes.Files.Config("members", Cfg.Folder(Cfg.Perms(rwDeny: ["a"])));

        Assert.Equal(ReadOnly, _notes.Get(Users.Anonymous, "x.md"));
        Assert.Equal(ReadOnly, _notes.Get(Users.Nobody, "x.md"));
        Assert.True(_notes.Get(Users.Alice, "x.md").CanWrite);

        // Denying a denies all users, those in a group included
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Anonymous, "members/x.md"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Nobody, "members/x.md"));
        Assert.False(_notes.Get(Users.Alice, "members/x.md").CanRead);
    }

    [Fact]
    public void Only_users_who_are_logged_in_get_what_is_given_to_l()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["l"], cdCreateOnly: ["l"])));

        EntryAccessInfo expected = new(ReadAccess.ReadAndWrite, CreateAccess.Create);
        Assert.Equal(expected, _notes.Get(Users.Nobody, "x.md"));
        Assert.Equal(expected, _notes.Get(Users.Alice, "x.md"));
        Assert.Equal(EntryAccessInfo.None, _notes.Get(Users.Anonymous, "x.md"));
    }

    [Fact]
    public void Changes_of_the_configuration_take_effect_at_once()
    {
        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwReadOnly: ["g:team"])));
        Assert.True(_notes.Get(Users.Alice, "x.md").CanRead);

        _notes.Files.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwDeny: ["g:team"])));
        Assert.False(_notes.Get(Users.Alice, "x.md").CanRead);

        File.Delete(Path.Combine(_notes.Root, ".config.json"));
        Assert.False(_notes.Get(Users.Alice, "x.md").CanRead);
    }
}
