using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration;
using Notes.Features.Configuration.Migrations;

namespace Notes.Tests.Unit;

/// <summary>The <c>archive</c> block of a folder's configuration, and the migration to 1.5.0 that adds it.</summary>
public sealed class ArchiveConfigTests
{
    private static readonly ConfigMigrator Migrator = new(ConfigVersions.Parse(Cfg.Version), ConfigMigrations.All, NullLogger<ConfigMigrator>.Instance);

    private static ParseResult<FolderConfig> Parse(string archive)
        => ConfigParser.ParseFolder($$"""{ "perms": { "inherit": true }, "gitignore": false, "archive": {{archive}}, "files": [] }""",
            existingFiles: null);

    [Fact]
    public void Without_it_a_folder_is_not_archived()
    {
        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder("""{ "perms": { "inherit": true }, "gitignore": false, "files": [] }""",
            existingFiles: null);

        Assert.True(parsed.IsValid, parsed.ErrorText);
        Assert.False(parsed.Value!.Archive.Enabled);
    }

    [Theory]
    [InlineData("""{ "enabled": true }""", ArchiveFormat.SevenZip, ArchiveCompression.High)]
    [InlineData("""{ "enabled": true, "filetype": "zip", "compressionLevel": 0 }""", ArchiveFormat.Zip, ArchiveCompression.Store)]
    [InlineData("""{ "enabled": true, "filetype": "7z", "compressionLevel": 0 }""", ArchiveFormat.SevenZip, ArchiveCompression.Min)]
    [InlineData("""{ "enabled": true, "compressionLevel": 1 }""", ArchiveFormat.SevenZip, ArchiveCompression.Medium)]
    [InlineData("""{ "enabled": true, "filetype": "zip", "compressionLevel": 3, "encrypted": false, "onLowestLevel": false }""",
        ArchiveFormat.Zip, ArchiveCompression.Max)]
    public void Only_enabled_is_needed_and_the_rest_has_defaults(string archive, ArchiveFormat format, ArchiveCompression compression)
    {
        ParseResult<FolderConfig> parsed = Parse(archive);

        Assert.True(parsed.IsValid, parsed.ErrorText);
        Assert.Equal(new ArchiveSettings(true, format, compression, false, false), parsed.Value!.Archive);
    }

    [Theory]
    [InlineData("""{ }""", "archive.enabled: is required")]
    [InlineData("""{ "enabled": "yes" }""", "archive.enabled")]
    [InlineData("""{ "enabled": true, "filetype": "rar" }""", "archive.filetype")]
    [InlineData("""{ "enabled": true, "compressionLevel": 4 }""", "archive.compressionLevel")]
    [InlineData("""{ "enabled": true, "compressionLevel": 1.5 }""", "archive.compressionLevel")]
    [InlineData("""{ "enabled": true, "encrypted": true }""", "archive.encrypted")]
    [InlineData("""{ "enabled": true, "onLowestLevel": true }""", "archive.onLowestLevel")]
    [InlineData("""{ "enabled": true, "level": 1 }""", "archive.level")]
    [InlineData("""true""", "archive")]
    public void What_is_wrong_with_it_is_said(string archive, string where)
    {
        ParseResult<FolderConfig> parsed = Parse(archive);

        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, error => error.StartsWith(where, StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_configuration_has_it_with_every_setting_and_a_comment()
    {
        string text = ConfigText.Default(["a.md"], ConfigVersions.Current);

        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(text, existingFiles: null);
        Assert.True(parsed.IsValid, parsed.ErrorText);
        JsonObject archive = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!
            ["archive"]!.AsObject();
        Assert.Equal(["enabled", "filetype", "compressionLevel", "encrypted", "onLowestLevel"], archive.Select(property => property.Key));
        Assert.Matches(@"//[^\n]*\n    ""archive""", text);
    }

    [Fact]
    public void An_older_file_gets_it_disabled_before_files_and_keeps_its_comments()
    {
        const string old = """
                           {
                               "version": "1.4.0",
                               "perms": { "inherit": true }, // mine
                               "gitignore": false,
                               "password": null,
                               "files": []
                           }
                           """;

        MigrationResult result = Migrator.Migrate(old);

        Assert.Null(result.Error);
        Assert.Contains("// mine", result.Text);
        Assert.Matches(@"""password"": null,\n(    //[^\n]*\n)+    ""archive"": \{[^\n]*\},\n    ""files""", result.Text);
        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(result.Text, existingFiles: null);
        Assert.True(parsed.IsValid, parsed.ErrorText);
        Assert.False(parsed.Value!.Archive.Enabled);
    }

    [Fact]
    public void A_folder_that_has_it_already_keeps_its_own()
    {
        string text = """{ "perms": { "inherit": true }, "gitignore": false, "archive": { "enabled": true }, "files": [] }""";

        Assert.Equal(text, ConfigText.AddDisabledArchive(text));
    }
}
