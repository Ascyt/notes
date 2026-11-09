using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using notes.Features.Permissions;
using notes.Features.Permissions.Migrations;
using Semver;

namespace notes.Tests.Unit;

/// <summary>Bringing a configuration from an older version of the format up to the current one.</summary>
public sealed class ConfigMigratorTests
{
    /// <summary>A migration made of a lambda.</summary>
    private sealed class Step(string version, Action<JsonObject> apply) : IConfigMigration
    {
        public SemVersion Version { get; } = ConfigVersions.Parse(version);
        public string Description => "test step " + Version;
        public void Apply(JsonObject root) => apply(root);
    }

    /// <summary>A step whose version has a pre-release part, which versions of the format never have.</summary>
    private sealed class PrereleaseStep : IConfigMigration
    {
        public SemVersion Version { get; } = SemVersion.ParsedFrom(1, 0, 0, "beta");
        public string Description => "pre-release";
        public void Apply(JsonObject root)
        {
        }
    }

    private static ConfigMigrator Migrator(string current, params IConfigMigration[] steps)
        => new(ConfigVersions.Parse(current), steps, NullLogger<ConfigMigrator>.Instance);

    private const string Legacy = """{ "perms": { "inherit": true }, "files": [] }""";

    // The steps of a configuration that, at 1.1.0, gets a required "owner" and, at 1.2.0, has "perms" moved into "access"
    private static readonly Step AddOwner = new("1.1.0", root => root["owner"] = "nobody");
    private static readonly Step MoveToAccess = new("1.2.0", root =>
    {
        JsonNode perms = root["perms"] ?? throw new InvalidOperationException("no perms");
        _ = root.Remove("perms");
        root["access"] = new JsonObject { ["perms"] = perms };
    });

    private static string Version(string text)
        => JsonNode.Parse(text)!["version"]!.GetValue<string>();

    [Fact]
    public void Text_of_the_current_version_is_left_as_it_is()
    {
        string text = "{ \"version\": \"1.0.0\", // kept\n \"perms\": { \"inherit\": true }, \"files\": [] }";

        MigrationResult result = Migrator("1.0.0").Migrate(text);

        Assert.Equal(text, result.Text);
        Assert.False(result.Changed);
        Assert.Null(result.Error);
        Assert.Equal(new SemVersion(1, 0, 0), result.From);
    }

    [Fact]
    public void Text_without_a_version_gets_the_current_one_and_keeps_its_comments_if_no_step_applies()
    {
        string text = "{\n    // kept\n    \"perms\": { \"inherit\": true },\n    \"files\": []\n}";

        MigrationResult result = Migrator("1.0.0").Migrate(text);

        Assert.True(result.Changed);
        Assert.Equal(ConfigVersions.Zero, result.From);
        Assert.Equal("{\n    \"version\": \"1.0.0\",\n    // kept\n    \"perms\": { \"inherit\": true },\n    \"files\": []\n}", result.Text);
        Assert.True(PermissionParser.ParseFolder(result.Text, existingFiles: null).IsValid);
    }

    [Fact]
    public void A_newer_version_with_no_step_between_only_changes_the_version()
    {
        string text = """{ "version": "1.0.0", "perms": { "inherit": true }, "files": [] }""";

        MigrationResult result = Migrator("1.0.5").Migrate(text);

        Assert.Equal("""{ "version": "1.0.5", "perms": { "inherit": true }, "files": [] }""", result.Text);
    }

    [Fact]
    public void Every_step_from_the_version_of_the_text_up_to_the_current_one_is_applied_in_order()
    {
        // Given in the wrong order: they are sorted by version
        ConfigMigrator migrator = Migrator("1.2.0", MoveToAccess, AddOwner);

        MigrationResult result = migrator.Migrate(Legacy);

        Assert.Null(result.Error);
        JsonNode migrated = JsonNode.Parse(result.Text)!;
        Assert.Equal("1.2.0", Version(result.Text));
        Assert.Equal("nobody", (string?)migrated["owner"]);
        Assert.Null(migrated["perms"]);
        Assert.True((bool?)migrated["access"]!["perms"]!["inherit"]);
        // The version stays the first property
        Assert.Equal("version", ((IDictionary<string, JsonNode?>)migrated.AsObject()).Keys.First());
    }

    [Fact]
    public void Only_the_steps_after_the_version_of_the_text_are_applied()
    {
        int ownerRuns = 0;
        Step countingOwner = new("1.1.0", root => { ownerRuns++; AddOwner.Apply(root); });
        ConfigMigrator migrator = Migrator("1.2.0", countingOwner, MoveToAccess);
        string text = """{ "version": "1.1.0", "owner": "alice", "perms": { "inherit": true }, "files": [] }""";

        MigrationResult result = migrator.Migrate(text);

        Assert.Equal(0, ownerRuns);
        Assert.Equal("alice", (string?)JsonNode.Parse(result.Text)!["owner"]);
        Assert.Equal("1.2.0", Version(result.Text));
    }

    [Fact]
    public void The_version_is_raised_to_the_current_one_when_the_last_step_is_older()
    {
        MigrationResult result = Migrator("1.5.2", AddOwner).Migrate(Legacy);

        Assert.Equal("1.5.2", Version(result.Text));
        Assert.Equal("nobody", (string?)JsonNode.Parse(result.Text)!["owner"]);
    }

    [Fact]
    public void A_migrated_text_is_migrated_no_further()
    {
        ConfigMigrator migrator = Migrator("1.2.0", AddOwner, MoveToAccess);
        string once = migrator.Migrate(Legacy).Text;

        MigrationResult again = migrator.Migrate(once);

        Assert.False(again.Changed);
        Assert.Equal(once, again.Text);
    }

    [Fact]
    public void Text_of_a_newer_version_is_an_error_and_not_changed()
    {
        string text = """{ "version": "2.0.0", "perms": { "inherit": true }, "files": [] }""";

        MigrationResult result = Migrator("1.0.0").Migrate(text);

        Assert.NotNull(result.Error);
        Assert.Contains("2.0.0", result.Error);
        Assert.Equal(text, result.Text);
        Assert.False(result.Changed);
    }

    [Fact]
    public void A_step_that_fails_is_an_error_and_leaves_the_text_as_it_was()
    {
        Step failing = new("1.1.0", _ => throw new InvalidOperationException("no owner to move"));

        MigrationResult result = Migrator("1.1.0", failing).Migrate(Legacy);

        Assert.NotNull(result.Error);
        Assert.Contains("1.1.0", result.Error);
        Assert.Contains("no owner to move", result.Error);
        Assert.Equal(Legacy, result.Text);
        Assert.False(result.Changed);
    }

    [Fact]
    public void A_step_that_throws_anything_else_is_an_error_too()
    {
        Step failing = new("1.1.0", root => _ = root["perms"]!["inherit"]!["x"]!["y"]);

        Assert.NotNull(Migrator("1.1.0", failing).Migrate(Legacy).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("""{ "version": 1, "perms": {} }""")]
    [InlineData("""{ "version": "1.0", "perms": {} }""")]
    [InlineData("""{ "version": null }""")]
    public void Text_that_is_not_a_configuration_is_returned_as_it_is_for_the_parser_to_report(string text)
    {
        MigrationResult result = Migrator("1.1.0", AddOwner).Migrate(text);

        Assert.Equal(text, result.Text);
        Assert.Null(result.From);
        Assert.Null(result.Error);
        Assert.False(result.Changed);

        // (And the parser does report it)
        Assert.False(PermissionParser.ParseFolder(text, existingFiles: null).IsValid);
    }

    [Fact]
    public void Steps_that_do_not_fit_the_current_version_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Migrator("1.0.0", AddOwner));
        Assert.Throws<InvalidOperationException>(() => Migrator("1.1.0", AddOwner, new Step("1.1.0", _ => { })));
        Assert.Throws<InvalidOperationException>(() => Migrator("1.0.0", new Step("0.0.0", _ => { })));
        Assert.Throws<InvalidOperationException>(() => new ConfigMigrator(new SemVersion(1, 0, 0), [new PrereleaseStep()], NullLogger<ConfigMigrator>.Instance));
        _ = Migrator("1.1.0", AddOwner);
    }
}
