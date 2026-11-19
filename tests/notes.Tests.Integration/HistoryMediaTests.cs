using System.Net;
using ImageMagick;
using LibGit2Sharp;

namespace notes.Tests.Integration;

/// <summary>The media of a directory in an earlier state and in the changes of a commit: their thumbnails and the way from one to the next, as they were.</summary>
public sealed class HistoryMediaTests(NotesAppFactory app) : HistoryStateTest(app)
{
    private static byte[] Png(uint width, uint height)
    {
        using MagickImage image = new(MagickColors.Green, width, height) { Format = MagickFormat.Png };
        return image.ToByteArray();
    }

    private string CommitAndGetId()
    {
        _ = CommitNow();
        using Repository repository = new(History.GitDirectory);
        return repository.Head.Tip.Sha;
    }

    [Fact]
    public async Task Thumbnails_and_the_grid_are_of_the_state()
    {
        _ = Tree.File("docs/photo.png", Png(640, 640));
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset then = CommitNow();
        _ = Tree.File("docs/photo.png", Png(10, 10));
        _ = CommitNow();

        HttpResponseMessage thumbnail = await Client.GetAsync(Users.Alice, "/docs/photo.png?thumbnail&" + At(then));
        await AssertStatus(HttpStatusCode.OK, thumbnail);
        using (MagickImage image = new(await thumbnail.Content.ReadAsByteArrayAsync(Cancellation)))
            Assert.Equal(320u, image.Width); // the large one it was then, not the small one it is now

        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs?" + At(then)));
        Assert.Contains("src=\"/docs/photo.png?thumbnail&amp;" + At(then) + "\"", listing);
    }

    [Fact]
    public async Task A_media_file_as_it_was_leads_to_the_media_that_were_next_to_it()
    {
        _ = Tree.File("docs/a.png", Png(10, 10));
        _ = Tree.File("docs/c.png", Png(10, 10));
        SetRoot(historyAllow: ["g:team"]);
        DateTimeOffset then = CommitNow();
        _ = Tree.File("docs/b.png", Png(10, 10)); // came later, so it isn't between them then
        _ = CommitNow();

        string page = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/c.png?" + At(then)));
        // The card of the state adds the state to the link in the browser
        Assert.Matches(@"<a id=""next-media-button"" href=""/docs/a.png""", page);
        Assert.Matches(@"<button id=""previous-media-button""[^>]*data-disabled-reason=""\S", page);
    }

    [Fact]
    public async Task In_the_changes_of_a_commit_a_deleted_image_shows_the_side_before_it()
    {
        _ = Tree.File("docs/kept.png", Png(10, 10));
        _ = Tree.File("docs/gone.png", Png(10, 10));
        SetRoot(historyAllow: ["g:team"]);
        _ = CommitNow();
        File.Delete(Tree.Physical("docs/gone.png"));
        _ = Tree.File("docs/new.png", Png(10, 10));
        string commit = CommitAndGetId();

        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs?commit=" + commit + "&changes"));
        Assert.Contains("src=\"/docs/gone.png?thumbnail&amp;commit=" + commit + "&amp;changes&amp;previous\"", listing);
        Assert.Contains("src=\"/docs/new.png?thumbnail&amp;commit=" + commit + "&amp;changes\"", listing);

        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/gone.png?thumbnail&commit=" + commit + "&changes&previous"));
        await AssertStatus(HttpStatusCode.OK, await Client.GetAsync(Users.Alice, "/docs/new.png?thumbnail&commit=" + commit + "&changes"));
        // After it, it is gone
        await AssertStatus(HttpStatusCode.NotFound, await Client.GetAsync(Users.Alice, "/docs/gone.png?thumbnail&commit=" + commit + "&changes"));
    }
}
