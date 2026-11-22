using System.Net;
using System.Text;

namespace Notes.Tests.Integration;

/// <summary>What a request for an archive may send (user input: the query, "How to make an archive", "Passwords of archives").</summary>
public sealed class ArchiveInputTests(NotesAppFactory app) : ArchiveTest(app)
{
    [Theory]
    [InlineData("7z", "store")] // the 7z writer always compresses
    [InlineData("zip", "min")] // the lowest level of a 7z
    [InlineData("rar", "high")]
    [InlineData("7Z", "high")]
    [InlineData("zip", "ultra")]
    [InlineData(null, "high")]
    [InlineData("zip", null)]
    public async Task Options_that_are_none_of_the_dialog_are_refused(string? format, string? compression)
    {
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice, "/docs?archive=directory",
            new { format, compression }));
        Assert.Empty(ArchiveFiles());
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("tab\there")]
    [InlineData("nul\0")]
    public async Task A_password_with_control_characters_is_refused(string password)
        => await AssertStatus(HttpStatusCode.BadRequest, await PrepareAsync(Users.Alice, "/docs", "directory", password: password));

    [Fact]
    public async Task A_password_may_be_as_long_as_the_limit_and_no_longer()
    {
        string longest = new('p', Notes.Features.Components.Archive.ArchiveOptions.MaxPasswordLength);

        await AssertStatus(HttpStatusCode.OK, await PrepareAsync(Users.Alice, "/docs", "directory", password: longest));
        await AssertStatus(HttpStatusCode.BadRequest, await PrepareAsync(Users.Alice, "/docs", "directory", password: longest + "p"));
    }

    [Fact]
    public async Task An_empty_password_is_none()
    {
        (_, byte[] bytes) = await PrepareAndDownloadAsync(Users.Alice, "/docs", "directory", "7z", "medium", "");

        Assert.Equal("a", SevenZipContents(bytes)["a.md"]);
    }

    [Theory]
    [InlineData("files")]
    [InlineData("")]
    [InlineData("DIRECTORY")]
    public async Task What_to_archive_is_directory_or_selection(string kind)
        => await AssertStatus(HttpStatusCode.BadRequest, await PrepareAsync(Users.Alice, "/docs", kind));

    [Fact]
    public async Task A_body_that_is_no_json_or_too_large_is_refused()
    {
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostAsync(Users.Alice, "/docs?archive=directory", "format=7z"));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.SendAsync(Users.Alice, HttpMethod.Post, "/docs?archive=directory",
            new StringContent("{ \"format\": ", Encoding.UTF8, "application/json")));
        await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice, "/docs?archive=directory",
            new { format = "7z", compression = "high", padding = new string(' ', 5000) }));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    public async Task The_id_of_a_download_is_a_guid(string id)
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.GetAsync(Users.Alice, "/docs?archive=" + Uri.EscapeDataString(id)));

    [Fact]
    public async Task Without_the_antiforgery_token_nothing_is_made()
    {
        HttpResponseMessage response = await Client.PostJsonAsync(Users.Alice, "/docs?archive=directory", new { format = "7z", compression = "high" },
            antiforgery: false);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Empty(ArchiveFiles());
    }

    [Fact]
    public async Task An_earlier_state_is_not_archived_with_options()
        => await AssertStatus(HttpStatusCode.BadRequest, await Client.PostJsonAsync(Users.Alice,
            "/docs?archive=directory&before=2026-01-01T00:00:00Z", new { format = "7z", compression = "high" }));

    [Fact]
    public async Task Only_one_archive_is_made_for_a_user_at_a_time()
    {
        Notes.Features.Components.Archive.PreparedArchives archives = (Notes.Features.Components.Archive.PreparedArchives)App.Services
            .GetService(typeof(Notes.Features.Components.Archive.PreparedArchives))!;
        using IDisposable? claim = archives.TryClaimMaking(Users.Alice.Sub!);
        Assert.NotNull(claim);

        await AssertStatus(HttpStatusCode.Conflict, await PrepareAsync(Users.Alice, "/docs", "directory"));
        await AssertStatus(HttpStatusCode.OK, await PrepareAsync(Users.Carol, "/docs", "directory"));
    }
}
