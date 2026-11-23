using System.Text.RegularExpressions;

namespace Notes.Tests.Integration;

/// <summary>The open lock that marks an entry whose password is unlocked, in listings and the breadcrumb.</summary>
public sealed partial class PasswordMarkTests(NotesAppFactory app) : PasswordTest(app)
{
    private static int MarksIn(string body) => UnlockedMark().Count(body);

    [GeneratedRegex("data-unlocked-mark")]
    private static partial Regex UnlockedMark();

    [Fact]
    public async Task Nothing_is_marked_as_unlocked_before_it_is()
    {
        Assert.Equal(0, MarksIn(await BodyOf(await Client.GetAsync(Users.Alice, "/"))));
        Assert.Equal(0, MarksIn(await BodyOf(await Client.GetAsync(Users.Alice, "/papers"))));
    }

    [Fact]
    public async Task An_unlocked_directory_is_marked_in_the_listing_and_only_it()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/safe", SafePassword);

        string root = await BodyOf(await GetWithCookieAsync(Users.Alice, "/", cookie!));

        Assert.Equal(1, MarksIn(root));
        Assert.DoesNotContain("data-password-locked-entry=\"safe\"", root);
    }

    [Fact]
    public async Task The_breadcrumb_marks_the_directory_that_has_the_password_not_what_is_inside()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/safe", SafePassword);

        string deeper = await BodyOf(await GetWithCookieAsync(Users.Alice, "/safe/deeper", cookie!));
        string inside = await BodyOf(await GetWithCookieAsync(Users.Alice, "/safe/inside.md", cookie!));

        Assert.Equal(1, MarksIn(deeper));
        Assert.Matches(@"breadcrumb-name"">safe\s*<i[^>]*data-unlocked-mark", deeper);
        Assert.Equal(1, MarksIn(inside));
    }

    [Fact]
    public async Task An_unlocked_file_is_marked_in_its_listing_and_on_its_page()
    {
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/papers/pw.md", FilePassword);

        Assert.Equal(1, MarksIn(await BodyOf(await GetWithCookieAsync(Users.Alice, "/papers", cookie!))));
        Assert.Equal(1, MarksIn(await BodyOf(await GetWithCookieAsync(Users.Alice, "/papers/pw.md", cookie!))));
    }

    [Fact]
    public async Task A_password_brought_in_the_header_marks_the_entry_too()
        => Assert.Equal(1, MarksIn(await BodyOf(await GetWithPasswordAsync(Users.Alice, "/safe", SafePassword))));
}
