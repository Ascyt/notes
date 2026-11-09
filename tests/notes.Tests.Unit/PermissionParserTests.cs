using notes.Features.Permissions;

namespace notes.Tests.Unit;

public sealed class PermissionParserTests
{
    private static ParseResult<FolderConfig> Folder(string text, IReadOnlySet<string>? files = null)
        => PermissionParser.ParseFolder(text, files);

    private static void AssertInvalid(string text, string expectedError, IReadOnlySet<string>? files = null)
    {
        ParseResult<FolderConfig> result = Folder(text, files);
        Assert.False(result.IsValid, "The configuration should be invalid:\n" + text);
        Assert.Contains(result.Errors, error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_example_of_the_documentation_is_valid()
    {
        const string text = """
            {
                "perms": {
                    "inherit": true,                      // required
                    "rwAllow":      ["g:team-a"],         // may read and write
                    "rwReadOnly":   ["g:team-b", "r:viewer", "u:alice"],   // may read
                    "rwDeny":       [],                   // may not read
                    "cdAllow":      ["g:team-a"],         // may create and delete
                    "cdCreateOnly": ["u:alice"],          // may create
                    "cdDeny":       []                    // may neither
                },
                "files": [                                // required: one entry per file, kept up to date by the app
                    { "name": "plan.md", "perms": { "inherit": true } },
                    { "name": "secret.md", "perms": { "inherit": false, "rwAllow": ["u:alice"] } }
                ]
            }
            """;

        ParseResult<FolderConfig> result = Folder(text);

        Assert.True(result.IsValid, result.ErrorText);
        Assert.True(result.Value!.Perms.Inherit);
        Assert.Contains("g:team-a", result.Value.Perms.RwAllow);
        Assert.False(result.Value.FindFile("SECRET.md")!.Perms.Inherit);
        Assert.Null(result.Value.FindFile("other.md"));
    }

    [Fact]
    public void Block_comments_are_allowed()
        => Assert.True(Folder("""/* a */ { "perms": { /* b */ "inherit": false }, "files": [] } /* c */""").IsValid);

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("{", "Not valid JSON")]
    [InlineData("[]", "must be an object")]
    [InlineData("null", "must be an object")]
    [InlineData("""{ "perms": { "inherit": true } }""", "files: is required")]
    [InlineData("""{ "files": [] }""", "perms: is required")]
    [InlineData("""{ "perms": {}, "files": [] }""", "perms.inherit: is required")]
    [InlineData("""{ "perms": { "inherit": "true" }, "files": [] }""", "must be true or false")]
    [InlineData("""{ "perms": { "inherit": 1 }, "files": [] }""", "must be true or false")]
    [InlineData("""{ "perms": { "inherit": true, "rwallow": [] }, "files": [] }""", "unknown property")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [], "extra": 1 }""", "unknown property")]
    [InlineData("""{ "perms": { "inherit": true }, "perms": { "inherit": false }, "files": [] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true, "inherit": false }, "files": [] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": "g:a" }, "files": [] }""", "must be an array")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": [1] }, "files": [] }""", "must be a string")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["alice"] }, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["x:alice"] }, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:"] }, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["U:alice"] }, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u: alice"] }, "files": [] }""", "spaces")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:alice "] }, "files": [] }""", "spaces")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:al\u0000ice"] }, "files": [] }""", "control characters")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["A"] }, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:alice", "u:ALICE"] }, "files": [] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["g:a"], "rwDeny": ["g:a"] }, "files": [] }""", "exclude each other")]
    [InlineData("""{ "perms": { "inherit": true, "rwReadOnly": ["u:Bob"], "rwDeny": ["u:bob"] }, "files": [] }""", "exclude each other")]
    [InlineData("""{ "perms": { "inherit": true, "cdAllow": ["r:x"], "cdCreateOnly": ["r:x"] }, "files": [] }""", "exclude each other")]
    [InlineData("""{ "perms": { "inherit": true }, "files": {} }""", "must be an array")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ "a.md" ] }""", "must be an object")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "name": "a.md" } ] }""", "perms: is required")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "perms": { "inherit": true } } ] }""", "name: is required")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "name": 1, "perms": { "inherit": true } } ] }""", "must be a string")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "name": "a.md", "perms": { "inherit": true } }, { "name": "A.MD", "perms": { "inherit": true } } ] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "name": "../a.md", "perms": { "inherit": true } } ] }""", "not a name a file can have")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "name": ".config.json", "perms": { "inherit": true } } ] }""", "not a name a file can have")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "name": "_", "perms": { "inherit": true } } ] }""", "not a name a file can have")]
    [InlineData("""{ "perms": { "inherit": true }, "files": [ { "name": "a.md", "perms": { "inherit": true }, "x": 1 } ] }""", "unknown property")]
    [InlineData("""{ "perms": { "inherit": true, }, "files": [] }""", "Not valid JSON")]
    [InlineData("""{ 'perms': { 'inherit': true }, 'files': [] }""", "Not valid JSON")]
    public void Invalid_configurations_say_why(string text, string expectedError)
        => AssertInvalid(text, expectedError);

    [Theory]
    [InlineData("rwAllow")]
    [InlineData("cdAllow")]
    [InlineData("cdCreateOnly")]
    public void A_can_never_be_given_anything_that_changes_the_notes(string list)
    {
        AssertInvalid($$"""{ "perms": { "inherit": true, "{{list}}": ["a"] }, "files": [] }""", "can't be in this list");
        AssertInvalid($$"""{ "perms": { "inherit": true }, "files": [ { "name": "f.md", "perms": { "inherit": true, "{{list}}": ["a"] } } ] }""", "can't be in this list");
        Assert.False(PermissionParser.ParseFileBody($$"""{ "perms": { "inherit": true, "{{list}}": ["a"] } }""").IsValid);
    }

    [Theory]
    [InlineData("rwReadOnly")]
    [InlineData("rwDeny")]
    [InlineData("cdDeny")]
    public void A_may_be_allowed_to_read_or_be_denied(string list)
        => Assert.True(Folder($$"""{ "perms": { "inherit": true, "{{list}}": ["a"] }, "files": [] }""").IsValid);

    [Theory]
    [InlineData("rwAllow")]
    [InlineData("rwReadOnly")]
    [InlineData("rwDeny")]
    [InlineData("cdAllow")]
    [InlineData("cdCreateOnly")]
    [InlineData("cdDeny")]
    public void L_may_be_in_every_list(string list)
        => Assert.True(Folder($$"""{ "perms": { "inherit": true, "{{list}}": ["l"] }, "files": [] }""").IsValid);

    [Fact]
    public void A_principal_may_be_in_one_rw_list_and_one_cd_list()
        => Assert.True(Folder("""{ "perms": { "inherit": true, "rwDeny": ["g:a"], "cdAllow": ["g:a"] }, "files": [] }""").IsValid);

    [Fact]
    public void Saving_needs_every_file_entry_to_be_a_file_of_the_folder()
    {
        const string text = """{ "perms": { "inherit": true }, "files": [ { "name": "gone.md", "perms": { "inherit": true } } ] }""";

        AssertInvalid(text, "there is no file 'gone.md'", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "here.md" });
        // What is in force is read without the files: a file removed from outside doesn't break the folder
        Assert.True(Folder(text).IsValid);
    }

    [Fact]
    public void Too_long_configurations_are_refused()
    {
        string text = """{ "perms": { "inherit": true }, "files": [] }""" + new string(' ', PermissionParser.MaxLength);
        AssertInvalid(text, "too long");
    }

    [Fact]
    public void Too_deep_configurations_are_refused_without_crashing()
    {
        string text = """{ "perms": { "inherit": true }, "files": [] , "x": """ + new string('[', 100) + new string(']', 100) + "}";
        Assert.False(Folder(text).IsValid);
    }

    [Fact]
    public void Principals_are_stored_normalized()
    {
        ParseResult<FolderConfig> result = Folder("""{ "perms": { "inherit": true, "rwAllow": ["u:Alice", "g:Team", "r:Role"] }, "files": [] }""");

        Assert.True(result.IsValid, result.ErrorText);
        Assert.Equal(["g:Team", "r:Role", "u:alice"], result.Value!.Perms.RwAllow.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_body_of_a_file_is_only_its_perms()
    {
        Assert.True(PermissionParser.ParseFileBody("""{ "perms": { "inherit": false, "rwAllow": ["u:x"] } }""").IsValid);
        Assert.False(PermissionParser.ParseFileBody("""{ "name": "x.md", "perms": { "inherit": false } }""").IsValid);
        Assert.False(PermissionParser.ParseFileBody("""{ "perms": { "inherit": false }, "files": [] }""").IsValid);
    }

    [Fact]
    public void Error_messages_hide_the_parser_internals()
    {
        ParseResult<FolderConfig> result = Folder("{\n  \"perms\": ,\n}");

        string error = Assert.Single(result.Errors);
        // Says where (line 2), but nothing of the parser it comes from
        Assert.Matches(@"\b2\b", error);
        Assert.DoesNotContain("LineNumber", error);
        Assert.DoesNotContain("reader options", error);
    }
}
