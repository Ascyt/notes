using notes.Features.Permissions;

namespace notes.Tests.Unit;

/// <summary>Editing the configuration text without losing comments, and keeping it valid.</summary>
public sealed class ConfigTextTests
{
    private const string Commented = """
        {
            // who may do what
            "perms": {
                "inherit": true // keep
            },
            "files": [
                // the plan
                { "name": "plan.md", "perms": { "inherit": true } },
                { "name": "secret.md", "perms": { "inherit": false, "rwAllow": ["u:carol"] } } // carol's
            ]
        }
        """;

    private static void AssertValid(string? text)
    {
        Assert.NotNull(text);
        ParseResult<FolderConfig> parsed = PermissionParser.ParseFolder(text, existingFiles: null);
        Assert.True(parsed.IsValid, parsed.ErrorText + "\n" + text);
    }

    [Fact]
    public void The_default_is_valid_and_has_an_entry_for_every_file()
    {
        string text = ConfigText.Default(["b.md", "A.md", "it's ü & co.md"]);

        AssertValid(text);
        Assert.Equal(["A.md", "b.md", "it's ü & co.md"], ConfigText.FileNames(text));
        AssertValid(ConfigText.Default([]));
        Assert.True(PermissionParser.ParseFileBody(ConfigText.DefaultFileBody()).IsValid);
    }

    [Fact]
    public void Adding_a_file_keeps_the_comments()
    {
        string? text = ConfigText.AddFile(Commented, "new.md");

        AssertValid(text);
        Assert.Equal(["plan.md", "secret.md", "new.md"], ConfigText.FileNames(text!));
        Assert.Contains("// the plan", text);
        Assert.Contains("// carol's", text);
        Assert.Contains("// keep", text);
    }

    [Fact]
    public void Adding_a_file_that_has_an_entry_changes_nothing()
        => Assert.Equal(Commented, ConfigText.AddFile(Commented, "PLAN.md"));

    [Fact]
    public void Adding_to_an_empty_list()
    {
        string? text = ConfigText.AddFile("""{ "perms": { "inherit": true }, "files": [] }""", "a.md");
        AssertValid(text);
        Assert.Equal(["a.md"], ConfigText.FileNames(text!));
    }

    /// <summary>
    /// An empty list with a comment in it (as an admin may write it) must stay valid when the first file is added (no trailing comma): an
    /// invalid configuration closes the folder to everyone but admins.
    /// </summary>
    [Fact]
    public void Adding_to_an_empty_list_with_a_comment_keeps_it_valid()
    {
        string? withComment = ConfigText.AddFile("""{ "perms": { "inherit": true }, "files": [ /* none yet */ ] }""", "a.md");
        AssertValid(withComment);
        Assert.Equal(["a.md"], ConfigText.FileNames(withComment!));
    }

    [Theory]
    [InlineData("plan.md")]
    [InlineData("secret.md")]
    public void Removing_a_file_keeps_the_rest_valid(string name)
    {
        string? text = ConfigText.RemoveFile(Commented, name);

        AssertValid(text);
        Assert.DoesNotContain(name, ConfigText.FileNames(text!));
        Assert.Single(ConfigText.FileNames(text!)!);
    }

    [Fact]
    public void Removing_every_file_one_by_one()
    {
        string text = ConfigText.Default(["a.md", "b.md", "c.md"]);
        foreach (string name in new[] { "b.md", "c.md", "a.md" })
        {
            text = ConfigText.RemoveFile(text, name)!;
            AssertValid(text);
        }

        Assert.Empty(ConfigText.FileNames(text)!);
    }

    [Fact]
    public void Renaming_a_file_only_changes_its_name()
    {
        string? text = ConfigText.RenameFile(Commented, "secret.md", "renamed 'x'.md");

        AssertValid(text);
        Assert.Equal(["plan.md", "renamed 'x'.md"], ConfigText.FileNames(text!));
        Assert.Contains("// carol's", text);
        Assert.False(PermissionParser.ParseFolder(text!, null).Value!.FindFile("renamed 'x'.md")!.Perms.Inherit);
    }

    /// <summary>
    /// An entry stays when its file is removed from outside the app (security specification: entries apply by name, whether or not the file
    /// exists). Renaming another file to that name must not leave the folder with two entries of the same name, which makes the configuration
    /// invalid and closes the whole folder to everyone but admins. The left over entry goes; the file keeps its own.
    /// </summary>
    [Fact]
    public void Renaming_a_file_to_a_name_that_still_has_an_entry_keeps_the_configuration_valid()
    {
        string? text = ConfigText.RenameFile(Commented, "plan.md", "secret.md");

        AssertValid(text);
        Assert.Equal(["secret.md"], ConfigText.FileNames(text!));
        Assert.True(PermissionParser.ParseFolder(text!, null).Value!.FindFile("secret.md")!.Perms.Inherit);
        Assert.Contains("// the plan", text);
    }

    [Fact]
    public void Renaming_a_file_without_an_entry_onto_a_left_over_one_removes_it()
    {
        string? text = ConfigText.RenameFile(Commented, "other.md", "secret.md");

        AssertValid(text);
        Assert.Equal(["plan.md"], ConfigText.FileNames(text!));
    }

    [Fact]
    public void Changing_only_the_case_keeps_the_entry()
    {
        string? text = ConfigText.RenameFile(Commented, "secret.md", "SECRET.md");

        Assert.Equal(["plan.md", "SECRET.md"], ConfigText.FileNames(text!));
        Assert.False(PermissionParser.ParseFolder(text!, null).Value!.FindFile("secret.md")!.Perms.Inherit);
    }

    [Fact]
    public void The_entry_of_a_file_without_its_name_and_back()
    {
        string? body = ConfigText.GetFileBody(Commented, "secret.md");

        Assert.NotNull(body);
        Assert.DoesNotContain("\"name\"", body);
        Assert.True(PermissionParser.ParseFileBody(body).IsValid, body);

        string? updated = ConfigText.SetFileBody(Commented, "secret.md", """
            {
                // still carol's, and alice reads
                "perms": { "inherit": false, "rwAllow": ["u:carol"], "rwReadOnly": ["u:alice"] }
            }
            """);
        AssertValid(updated);
        Assert.Contains("still carol's", updated);
        Assert.Contains("u:alice", PermissionParser.ParseFolder(updated!, null).Value!.FindFile("secret.md")!.Perms.RwReadOnly);
        Assert.Equal(["plan.md", "secret.md"], ConfigText.FileNames(updated!));
    }

    /// <summary>
    /// A comment above the opening brace is moved inside the object, on a line of its own: if the object is on one line, the rest of that
    /// line must not end up behind the "//".
    /// </summary>
    [Fact]
    public void A_comment_above_a_one_line_entry_of_a_file_is_kept_without_breaking_it()
    {
        string? updated = ConfigText.SetFileBody(Commented, "secret.md", """
            // still carol's
            { "perms": { "inherit": false, "rwAllow": ["u:carol"] } }
            """);

        AssertValid(updated);
        Assert.Contains("still carol's", updated);
    }

    [Fact]
    public void Setting_the_entry_of_a_file_without_one_adds_it()
    {
        string? updated = ConfigText.SetFileBody(Commented, "new.md", """{ "perms": { "inherit": false } }""");

        AssertValid(updated);
        Assert.Equal(["plan.md", "secret.md", "new.md"], ConfigText.FileNames(updated!));
    }

    [Fact]
    public void A_body_that_tries_to_set_its_own_name_does_not_get_another_files_entry()
    {
        // The name is the file's own: a body with "name" in it is refused by the parser before it gets here, and if it did get here the
        // result must not be valid with two names in one entry
        string? updated = ConfigText.SetFileBody(Commented, "plan.md", """{ "name": "secret.md", "perms": { "inherit": true, "rwAllow": ["u:mallory"] } }""");

        if (updated is not null)
            Assert.False(PermissionParser.ParseFolder(updated, null).IsValid);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{ "perms": { "inherit": true } }""")]
    [InlineData("""{ "perms": { "inherit": true }, "files": {} }""")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ 1 ] }""")]
    public void Text_that_can_not_be_edited_gives_null(string text)
    {
        Assert.Null(ConfigText.AddFile(text, "a.md"));
        Assert.Null(ConfigText.RemoveFile(text, "a.md"));
        Assert.Null(ConfigText.RenameFile(text, "a.md", "b.md"));
        Assert.Null(ConfigText.SetFileBody(text, "a.md", """{ "perms": { "inherit": true } }"""));
    }

    [Fact]
    public void A_body_that_is_not_an_object_gives_null()
        => Assert.Null(ConfigText.SetFileBody(Commented, "plan.md", "[]"));

    [Fact]
    public void Only_the_files_array_of_the_root_object_is_edited()
    {
        const string text = """{ "perms": { "inherit": true }, "files": [ { "name": "a.md", "perms": { "inherit": true } } ] }""";
        const string nested = """{ "x": { "files": [] }, "perms": { "inherit": true }, "files": [] }""";

        Assert.Equal(["a.md"], ConfigText.FileNames(text));
        string? added = ConfigText.AddFile(nested, "a.md");
        Assert.Contains("\"x\": { \"files\": [] }", added);
    }

    [Fact]
    public void Non_ascii_names_and_comments_survive()
    {
        string text = "{ \"perms\": { \"inherit\": true }, // ümlauts 🎉\n \"files\": [ { \"name\": \"日本.md\", \"perms\": { \"inherit\": true } } ] }";

        string? renamed = ConfigText.RenameFile(text, "日本.md", "ü.md");
        AssertValid(renamed);
        Assert.Contains("// ümlauts 🎉", renamed);
        Assert.Equal(["ü.md"], ConfigText.FileNames(renamed!));
        string? added = ConfigText.AddFile(renamed!, "🎉.md");
        Assert.Equal(["ü.md", "🎉.md"], ConfigText.FileNames(added!));
    }
}
