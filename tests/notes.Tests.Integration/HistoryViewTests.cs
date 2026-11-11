namespace notes.Tests.Integration;

/// <summary>Seeing earlier states of the notes (docs/development/history-specification.md): who gets the way to them.</summary>
public sealed class HistoryViewTests(NotesAppFactory app) : IntegrationTest(app)
{
    private const string Button = "id=\"history-button\"";
    private const string Modal = "id=\"history-modal\"";

    private async Task<string> PageAsync(TestUser user, string path)
        => await BodyOf(await Client.GetAsync(user, path));

    private async Task AssertOffered(TestUser user, string path, bool offered)
    {
        string body = await PageAsync(user, path);
        Assert.Equal(offered, body.Contains(Button));
        Assert.Equal(offered, body.Contains(Modal));
    }

    [Fact]
    public async Task Without_the_permission_nobody_but_admins_gets_the_way_to_the_history()
    {
        // The base tree gives the history to nobody
        foreach (TestUser user in new[] { Users.Alice, Users.Bob, Users.Anonymous })
        {
            await AssertOffered(user, "/", offered: false);
            await AssertOffered(user, "/docs", offered: false);
            await AssertOffered(user, "/readme.md", offered: false);
        }

        await AssertOffered(Users.Admin, "/", offered: true);
        await AssertOffered(Users.Admin, "/docs", offered: true);
        await AssertOffered(Users.Admin, "/readme.md", offered: true);
        await AssertOffered(Users.Admin, "/broken/x.md", offered: true);
    }

    [Fact]
    public async Task Those_who_are_given_the_history_get_the_way_to_it_on_directories_and_files()
    {
        Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["g:guests", "a", "u:carol"], cdAllow: ["g:team"], historyAllow: ["g:team"])));

        await AssertOffered(Users.Alice, "/", offered: true);
        await AssertOffered(Users.Alice, "/docs", offered: true);
        await AssertOffered(Users.Alice, "/docs/nested/b.md", offered: true);
        await AssertOffered(Users.Alice, "/readme.md?edit", offered: true);
        await AssertOffered(Users.Bob, "/readme.md", offered: false);
        await AssertOffered(Users.Anonymous, "/readme.md", offered: false);
    }

    [Fact]
    public async Task The_history_may_be_given_to_those_who_are_not_logged_in()
    {
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(historyAllow: ["a"])));

        await AssertOffered(Users.Anonymous, "/docs", offered: true);
        await AssertOffered(Users.Anonymous, "/docs/a.md", offered: true);
        await AssertOffered(Users.Bob, "/docs/a.md", offered: true);
        await AssertOffered(Users.Anonymous, "/readme.md", offered: false);
    }

    [Fact]
    public async Task A_deny_takes_the_way_away_where_it_applies()
    {
        Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["g:guests", "a", "u:carol"], cdAllow: ["g:team"], historyAllow: ["l"])));
        Tree.Config("docs", Cfg.Folder(Cfg.Perms(historyDeny: ["u:alice"]), ("a.md", Cfg.Perms(historyAllow: ["u:alice"]))));

        await AssertOffered(Users.Alice, "/docs", offered: false);
        await AssertOffered(Users.Alice, "/docs/nested/b.md", offered: false);
        // A file entry is more specific than its folder
        await AssertOffered(Users.Alice, "/docs/a.md", offered: true);
        await AssertOffered(Users.Carol, "/docs", offered: true);
    }

    [Fact]
    public async Task Read_comes_first_an_entry_that_can_not_be_read_offers_no_history()
    {
        Tree.Config("vault", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["u:carol"], cdAllow: ["u:carol"], historyAllow: ["u:carol", "u:alice"])));

        await AssertOffered(Users.Carol, "/vault", offered: true);
        // Alice is given the history, but may not read the directory: there is no page for her at all
        string body = await PageAsync(Users.Alice, "/vault");
        Assert.DoesNotContain(Button, body);
    }

    [Fact]
    public async Task What_the_history_leaves_out_has_no_way_to_it_and_admins_see_why()
    {
        Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["g:guests", "a", "u:carol"], cdAllow: ["g:team"], historyAllow: ["g:team"])));
        Tree.Config("docs", Cfg.Folder(gitignore: true, Cfg.Perms()));
        Tree.Config("docs/nested", Cfg.Folder(gitignore: false, Cfg.Perms()));

        foreach (string path in new[] { "/docs", "/docs/a.md" })
        {
            await AssertOffered(Users.Alice, path, offered: false);

            // Admins see the button, disabled with the reason, and have no dialog behind it
            string body = await PageAsync(Users.Admin, path);
            Assert.Matches(@"id=""history-button""[^>]* data-disabled-reason=""[^""]+""", body);
            Assert.DoesNotContain(Modal, body);
        }

        // A subdirectory that is kept again has its history
        await AssertOffered(Users.Alice, "/docs/nested", offered: true);
        await AssertOffered(Users.Alice, "/docs/nested/b.md", offered: true);
        await AssertOffered(Users.Alice, "/readme.md", offered: true);
    }

    [Fact]
    public async Task The_history_button_is_right_of_the_configuration_and_left_of_the_other_views_and_the_download()
    {
        static void AssertOrder(string body, params string[] ids)
        {
            int[] positions = [.. ids.Select(id => body.IndexOf($"id=\"{id}\"", StringComparison.Ordinal))];
            Assert.All(positions, position => Assert.True(position >= 0));
            Assert.Equal(positions.Order(), positions);
        }

        _ = Tree.File("docs/picture.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);

        AssertOrder(await PageAsync(Users.Admin, "/docs"), "config-button", "history-button", "download-button", "upload-button");
        AssertOrder(await PageAsync(Users.Admin, "/docs/a.md"), "config-button", "history-button", "download-button");
        AssertOrder(await PageAsync(Users.Admin, "/readme.md"), "edit-button", "config-button", "history-button", "pdf-button", "download-button");
        AssertOrder(await PageAsync(Users.Admin, "/docs/picture.png"), "config-button", "history-button", "raw-button", "download-button");
    }

    [Fact]
    public async Task The_configuration_page_offers_no_history()
        => Assert.DoesNotContain(Button, await PageAsync(Users.Admin, "/docs?config"));
}
