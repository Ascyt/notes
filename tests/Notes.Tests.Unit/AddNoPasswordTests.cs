using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Configuration;
using Notes.Features.Configuration.Migrations;

namespace Notes.Tests.Unit;

/// <summary>The migration to 1.4.0, which adds <c>"password": null</c> with its comment, applied by the app's own migrator.</summary>
public sealed class AddNoPasswordTests
{
    private static readonly ConfigMigrator Migrator = new(ConfigVersions.Parse(Cfg.Version), ConfigMigrations.All, NullLogger<ConfigMigrator>.Instance);

    private static FolderConfig Parsed(string text)
    {
        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(text, existingFiles: null);
        Assert.True(parsed.IsValid, parsed.ErrorText);
        return parsed.Value!;
    }

    [Fact]
    public void A_file_of_1_3_0_keeps_its_comments_and_gets_a_password_of_null_with_its_comment_before_files()
    {
        const string old = """
                           {
                               // mine
                               "version": "1.3.0",
                               "perms": { "inherit": true },
                               "gitignore": false, // kept in the history
                               // the files
                               "files": []
                           }
                           """;

        MigrationResult result = Migrator.Migrate(old);

        Assert.Null(result.Error);
        Assert.Contains("// mine", result.Text);
        Assert.Contains("\"gitignore\": false, // kept in the history\n", result.Text);
        // The comment above files stays right above it, the new lines come before that comment
        Assert.Contains("\"password\": null,\n    // the files\n    \"files\"", result.Text);
        Assert.Matches(@"\n    // [^\n]*passwordHash[^\n]*\n    // [^\n]*\n    ""password"": null,", result.Text);
        Assert.Equal(Cfg.Version, JsonNode.Parse(result.Text, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!["version"]!.GetValue<string>());
        Assert.Null(Parsed(result.Text).PasswordHash);
    }

    [Fact]
    public void A_folder_with_a_password_keeps_it_as_it_is()
    {
        const string old = """{ "version": "1.3.0", "perms": { "inherit": true }, "gitignore": false, "passwordHash": "$2a$12$abcdefghijklmnopqrstuuJ0zTGEo3tYJt9vqmWGkWKdKTiJXmEXm", "files": [] }""";

        MigrationResult result = Migrator.Migrate(old);

        Assert.Null(result.Error);
        Assert.DoesNotContain("\"password\"", result.Text);
        Assert.Contains("\"passwordHash\"", result.Text);
    }

    [Fact]
    public void A_folder_that_says_null_already_gets_no_second_one()
    {
        const string old = """{ "version": "1.3.0", "perms": { "inherit": true }, "gitignore": false, "password": null, "files": [] }""";

        MigrationResult result = Migrator.Migrate(old);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result.Text, "\"password\""));
    }

    [Fact]
    public void Files_on_one_line_get_it_on_that_line()
    {
        MigrationResult result = Migrator.Migrate("""{ "version": "1.3.0", "perms": { "inherit": true }, "gitignore": false, "files": [] }""");

        Assert.Equal(["version", "perms", "gitignore", "password", "files"], JsonNode.Parse(result.Text)!.AsObject().Select(property => property.Key));
        _ = Parsed(result.Text);
    }

    [Fact]
    public void A_file_of_before_versions_goes_through_both_migrations()
    {
        MigrationResult result = Migrator.Migrate("""{ "perms": { "inherit": true }, "files": [ { "name": "a.md", "perms": { "inherit": true } } ] }""");

        Assert.Null(result.Error);
        Assert.Equal(["version", "perms", "gitignore", "password", "files"], JsonNode.Parse(result.Text)!.AsObject().Select(property => property.Key));
        Assert.NotNull(Parsed(result.Text).FindFile("a.md"));
    }

    [Fact]
    public void Files_first_gets_it_after_the_brace()
    {
        string? text = ConfigText.AddNoPassword("{\n    \"files\": []\n}\n");

        Assert.NotNull(text);
        Assert.Matches("^\\{\n    // [^\n]*\n    // [^\n]*\n    \"password\": null,\n    \"files\": \\[\\]\n\\}\n$", text);
    }

    [Fact]
    public void Windows_line_breaks_are_used_for_the_new_lines_too()
    {
        string? text = ConfigText.AddNoPassword("{\r\n    \"gitignore\": false, // x\r\n    \"files\": []\r\n}\r\n");

        Assert.NotNull(text);
        Assert.Contains("\"gitignore\": false, // x\r\n    //", text);
        Assert.Contains("\"password\": null,\r\n    \"files\"", text);
        Assert.DoesNotMatch("[^\r]\n", text);
        _ = JsonNode.Parse(text, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
    }

    [Fact]
    public void Without_files_nothing_is_added()
    {
        const string text = """{ "perms": { "inherit": true } }""";

        Assert.Equal(text, ConfigText.AddNoPassword(text));
    }

    [Fact]
    public void The_default_configuration_needs_nothing_more()
    {
        string text = ConfigText.Default(["a.md"], ConfigVersions.Current);

        Assert.Equal(text, ConfigText.AddNoPassword(text));
    }
}
