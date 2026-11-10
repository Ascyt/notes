using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using notes.Features.Configuration;
using notes.Features.Configuration.Migrations;

namespace notes.Tests.Unit;

/// <summary>The migration to 1.1.0, which adds <c>gitignore</c>, applied by the app's own migrator.</summary>
public sealed class AddGitignoreTests
{
    private static readonly ConfigMigrator Migrator = new(ConfigVersions.Parse(Cfg.Version), ConfigMigrations.All, NullLogger<ConfigMigrator>.Instance);

    [Fact]
    public void A_file_of_1_0_0_keeps_its_folder_in_the_history()
    {
        const string old = """{ "version": "1.0.0", "perms": { "inherit": false }, "files": [ { "name": "a.md", "perms": { "inherit": true } } ] }""";

        MigrationResult result = Migrator.Migrate(old);

        Assert.Null(result.Error);
        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(result.Text, existingFiles: null);
        Assert.True(parsed.IsValid, parsed.ErrorText);
        Assert.False(parsed.Value!.Gitignore);
        Assert.False(parsed.Value.Perms.Inherit);
        Assert.NotNull(parsed.Value.FindFile("a.md"));
        Assert.Equal(Cfg.Version, JsonNode.Parse(result.Text)!["version"]!.GetValue<string>());
    }

    [Fact]
    public void It_comes_right_after_perms()
    {
        MigrationResult result = Migrator.Migrate("""{ "version": "1.0.0", "perms": { "inherit": true }, "files": [] }""");

        Assert.Equal(["version", "perms", "gitignore", "files"], JsonNode.Parse(result.Text)!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public void A_file_without_a_version_gets_it_too()
    {
        MigrationResult result = Migrator.Migrate("""{ "perms": { "inherit": true }, "files": [] }""");

        Assert.True(ConfigParser.ParseFolder(result.Text, existingFiles: null).IsValid);
    }

    [Fact]
    public void A_file_of_1_0_0_that_has_it_already_is_not_guessed_at()
    {
        MigrationResult result = Migrator.Migrate("""{ "version": "1.0.0", "perms": { "inherit": true }, "gitignore": true, "files": [] }""");

        Assert.NotNull(result.Error);
        Assert.False(result.Changed);
    }
}
