using System.Net;
using System.Text;

namespace Notes.Tests.Integration;

/// <summary>The rules for names users give new entries, and names in any case being the same name (docs/development/user-input.md).</summary>
public sealed class NewNameTests(NotesAppFactory app) : IntegrationTest(app)
{
    private Task<HttpResponseMessage> RenameAsync(TestUser user, string path, string newName)
        => Client.PostAsync(user, "/" + path + "?rename=" + Uri.EscapeDataString(newName));

    private async Task<HttpResponseMessage> UploadAsync(TestUser user, string directory, string name, string contents = "x")
    {
        HttpResponseMessage start = await Client.PostAsync(user, "/" + directory + "?upload=start");
        await AssertStatus(HttpStatusCode.OK, start);
        string id = await BodyOf(start);
        return await Client.PostBytesAsync(user, $"/{directory}?upload={id}&name={Uri.EscapeDataString(name)}&offset=0&last", Encoding.UTF8.GetBytes(contents));
    }

    public static TheoryData<string> RefusedNames =>
    [
        "line\nbreak.md", "tab\t.md", "bell\u0007.md", "delete\u007f.md", "c1\u0085.md",
        "reversed\u202Edm.exe", "isolate\u2066.md", "zero\u200Bwidth.md", "separator\u2028.md",
        "a:b.md", "a*b.md", "a?b.md", "a\"b.md", "a<b.md", "a>b.md", "a|b.md",
        "CON", "nul.txt", "Com1.md", "lpt9", "AUX .md",
        new string('a', 256)
    ];

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task Names_that_break_the_rules_are_refused_for_a_rename_even_for_admins(string name)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await RenameAsync(Users.Admin, "docs/a.md", name));
        Assert.Equal(before, Tree.Snapshot());
    }

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task Names_that_break_the_rules_are_refused_for_an_upload(string name)
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.BadRequest, await UploadAsync(Users.Alice, "docs", name));
        Assert.Equal(before, Tree.Snapshot());
    }

    [Theory]
    [InlineData("notes 2026 (draft) #3, 50% done.md")]
    [InlineData("Grüße an 日本.md")]
    [InlineData("family 👨\u200D👩\u200D👧.md")] // emoji are made with the zero-width joiner, which is allowed
    public async Task Unusual_but_harmless_names_are_allowed(string name)
    {
        await AssertStatus(HttpStatusCode.OK, await RenameAsync(Users.Alice, "docs/a.md", name));
        Assert.True(Tree.IsFile("docs/" + name));
    }

    [Fact]
    public async Task A_name_of_255_bytes_is_allowed_and_one_more_is_not()
    {
        // "é" takes two bytes: 127 of them and one "a" are 255
        string longest = new string('é', 127) + "a";
        Assert.Equal(255, Encoding.UTF8.GetByteCount(longest));

        await AssertStatus(HttpStatusCode.BadRequest, await RenameAsync(Users.Alice, "docs/a.md", longest + "a"));
        await AssertStatus(HttpStatusCode.OK, await RenameAsync(Users.Alice, "docs/a.md", longest));
    }

    [Fact]
    public async Task A_name_is_kept_in_composed_form()
    {
        // "e" followed by a combining accent, as some systems spell "é"
        string decomposed = "caf" + "e\u0301" + ".md";

        HttpResponseMessage response = await UploadAsync(Users.Alice, "docs", decomposed);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("docs/café.md", (await BodyOf(response)).Normalize(NormalizationForm.FormC));
        Assert.Contains("café.md", Directory.GetFiles(Tree.Physical("docs")).Select(Path.GetFileName));
    }

    // ---- The same name in another case

    [Fact]
    public async Task An_upload_never_takes_the_name_of_an_entry_in_another_case()
    {
        HttpResponseMessage response = await UploadAsync(Users.Alice, "docs", "A.MD", "new");

        Assert.Equal("docs/A (1).MD", await BodyOf(response));
        Assert.Equal("a", Tree.Read("docs/a.md"));
    }

    [Fact]
    public async Task A_rename_to_the_name_of_another_entry_in_another_case_is_a_conflict()
    {
        string before = Tree.Snapshot();

        await AssertStatus(HttpStatusCode.Conflict, await RenameAsync(Users.Alice, "docs/nested", "A.MD"));
        Assert.Equal(before, Tree.Snapshot());
    }

    // ---- A renamed file keeps its own permissions

    [Fact]
    public async Task A_renamed_file_keeps_its_own_permissions_under_its_new_name()
    {
        // Alice may only read view.md (its own entry), but may change the other files of the folder
        await AssertStatus(HttpStatusCode.OK, await RenameAsync(Users.Admin, "mixed/view.md", "seen.md"));

        await AssertStatus(HttpStatusCode.Forbidden, await Client.PostAsync(Users.Alice, "/mixed/seen.md", "changed"));
        Assert.Equal("view", Tree.Read("mixed/seen.md"));
        string config = Tree.ReadConfig("mixed")!;
        Assert.Contains("\"seen.md\"", config);
        Assert.DoesNotContain("\"view.md\"", config);
    }
}
