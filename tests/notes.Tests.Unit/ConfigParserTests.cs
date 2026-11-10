using notes.Features.Configuration;

namespace notes.Tests.Unit;

public sealed class ConfigParserTests
{
    private static ParseResult<FolderConfig> Folder(string text, IReadOnlySet<string>? files = null)
        => ConfigParser.ParseFolder(text, files);

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
                "gitignore": false,                       // required: whether the history leaves this folder out
                "files": [                               // required: one entry per file, kept up to date by the app
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
        => Assert.True(Folder("""/* a */ { "perms": { /* b */ "inherit": false }, "gitignore": false, "files": [] } /* c */""").IsValid);

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("{", "Not valid JSON")]
    [InlineData("[]", "must be an object")]
    [InlineData("null", "must be an object")]
    [InlineData("""{ "perms": { "inherit": true } }""", "files: is required")]
    [InlineData("""{ "files": [] }""", "perms: is required")]
    [InlineData("""{ "perms": {}, "gitignore": false, "files": [] }""", "perms.inherit: is required")]
    [InlineData("""{ "perms": { "inherit": "true" }, "gitignore": false, "files": [] }""", "must be true or false")]
    [InlineData("""{ "perms": { "inherit": 1 }, "gitignore": false, "files": [] }""", "must be true or false")]
    [InlineData("""{ "perms": { "inherit": true, "rwallow": [] }, "gitignore": false, "files": [] }""", "unknown property")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [], "extra": 1 }""", "unknown property")]
    [InlineData("""{ "perms": { "inherit": true }, "perms": { "inherit": false }, "gitignore": false, "files": [] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true, "inherit": false }, "gitignore": false, "files": [] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": "g:a" }, "gitignore": false, "files": [] }""", "must be an array")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": [1] }, "gitignore": false, "files": [] }""", "must be a string")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["alice"] }, "gitignore": false, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["x:alice"] }, "gitignore": false, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:"] }, "gitignore": false, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["U:alice"] }, "gitignore": false, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u: alice"] }, "gitignore": false, "files": [] }""", "spaces")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:alice "] }, "gitignore": false, "files": [] }""", "spaces")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:al\u0000ice"] }, "gitignore": false, "files": [] }""", "control characters")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["A"] }, "gitignore": false, "files": [] }""", "is not a principal")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["u:alice", "u:ALICE"] }, "gitignore": false, "files": [] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true, "rwAllow": ["g:a"], "rwDeny": ["g:a"] }, "gitignore": false, "files": [] }""", "exclude each other")]
    [InlineData("""{ "perms": { "inherit": true, "rwReadOnly": ["u:Bob"], "rwDeny": ["u:bob"] }, "gitignore": false, "files": [] }""", "exclude each other")]
    [InlineData("""{ "perms": { "inherit": true, "cdAllow": ["r:x"], "cdCreateOnly": ["r:x"] }, "gitignore": false, "files": [] }""", "exclude each other")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": {} }""", "must be an array")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ "a.md" ] }""", "must be an object")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "a.md" } ] }""", "perms: is required")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "perms": { "inherit": true } } ] }""", "name: is required")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": 1, "perms": { "inherit": true } } ] }""", "must be a string")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "a.md", "perms": { "inherit": true } }, { "name": "A.MD", "perms": { "inherit": true } } ] }""", "more than once")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "../a.md", "perms": { "inherit": true } } ] }""", "not a name a file can have")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": ".config.json", "perms": { "inherit": true } } ] }""", "not a name a file can have")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "_", "perms": { "inherit": true } } ] }""", "not a name a file can have")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "a.md", "perms": { "inherit": true }, "x": 1 } ] }""", "unknown property")]
    [InlineData("""{ "perms": { "inherit": true, }, "gitignore": false, "files": [] }""", "Not valid JSON")]
    [InlineData("""{ 'perms': { 'inherit': true }, 'files': [] }""", "Not valid JSON")]
    public void Invalid_configurations_say_why(string text, string expectedError)
        => AssertInvalid(text, expectedError);

    [Theory]
    [InlineData("rwAllow")]
    [InlineData("cdAllow")]
    [InlineData("cdCreateOnly")]
    public void A_can_never_be_given_anything_that_changes_the_notes(string list)
    {
        AssertInvalid($$"""{ "perms": { "inherit": true, "{{list}}": ["a"] }, "gitignore": false, "files": [] }""", "can't be in this list");
        AssertInvalid($$"""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "f.md", "perms": { "inherit": true, "{{list}}": ["a"] } } ] }""", "can't be in this list");
        Assert.False(ConfigParser.ParseFileBody($$"""{ "perms": { "inherit": true, "{{list}}": ["a"] } }""").IsValid);
    }

    [Theory]
    [InlineData("rwReadOnly")]
    [InlineData("rwDeny")]
    [InlineData("cdDeny")]
    public void A_may_be_allowed_to_read_or_be_denied(string list)
        => Assert.True(Folder($$"""{ "perms": { "inherit": true, "{{list}}": ["a"] }, "gitignore": false, "files": [] }""").IsValid);

    [Theory]
    [InlineData("rwAllow")]
    [InlineData("rwReadOnly")]
    [InlineData("rwDeny")]
    [InlineData("cdAllow")]
    [InlineData("cdCreateOnly")]
    [InlineData("cdDeny")]
    public void L_may_be_in_every_list(string list)
        => Assert.True(Folder($$"""{ "perms": { "inherit": true, "{{list}}": ["l"] }, "gitignore": false, "files": [] }""").IsValid);

    [Fact]
    public void A_principal_may_be_in_one_rw_list_and_one_cd_list()
        => Assert.True(Folder("""{ "perms": { "inherit": true, "rwDeny": ["g:a"], "cdAllow": ["g:a"] }, "gitignore": false, "files": [] }""").IsValid);

    [Fact]
    public void Saving_needs_every_file_entry_to_be_a_file_of_the_folder()
    {
        const string text = """{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "gone.md", "perms": { "inherit": true } } ] }""";

        AssertInvalid(text, "there is no file 'gone.md'", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "here.md" });
        // What is in force is read without the files: a file removed from outside doesn't break the folder
        Assert.True(Folder(text).IsValid);
    }

    [Fact]
    public void Too_long_configurations_are_refused()
    {
        string text = """{ "perms": { "inherit": true }, "gitignore": false, "files": [] }""" + new string(' ', ConfigParser.MaxLength);
        AssertInvalid(text, "too long");
    }

    [Fact]
    public void Too_deep_configurations_are_refused_without_crashing()
    {
        string text = """{ "perms": { "inherit": true }, "gitignore": false, "files": [] , "x": """ + new string('[', 100) + new string(']', 100) + "}";
        Assert.False(Folder(text).IsValid);
    }

    [Fact]
    public void Principals_are_stored_normalized()
    {
        ParseResult<FolderConfig> result = Folder("""{ "perms": { "inherit": true, "rwAllow": ["u:Alice", "g:Team", "r:Role"] }, "gitignore": false, "files": [] }""");

        Assert.True(result.IsValid, result.ErrorText);
        Assert.Equal(["g:Team", "r:Role", "u:alice"], result.Value!.Perms.RwAllow.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_body_of_a_file_is_only_its_perms()
    {
        Assert.True(ConfigParser.ParseFileBody("""{ "perms": { "inherit": false, "rwAllow": ["u:x"] } }""").IsValid);
        Assert.False(ConfigParser.ParseFileBody("""{ "name": "x.md", "perms": { "inherit": false } }""").IsValid);
        Assert.False(ConfigParser.ParseFileBody("""{ "perms": { "inherit": false }, "gitignore": false, "files": [] }""").IsValid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gitignore_is_read(bool gitignore)
        => Assert.Equal(gitignore, Folder(Cfg.Folder(gitignore, Cfg.Perms())).Value!.Gitignore);

    [Theory]
    [InlineData("""{ "perms": { "inherit": true }, "files": [] }""", "gitignore: is required")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": "true", "files": [] }""", "must be true or false")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": null, "files": [] }""", "must be true or false")]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "files": [ { "name": "a.md", "perms": { "inherit": true }, "gitignore": true } ] }""", "unknown property")]
    public void Gitignore_is_a_required_boolean_of_the_folder(string text, string expectedError)
        => AssertInvalid(text, expectedError);

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
