using System.Text.Json.Nodes;
using Semver;

namespace notes.Features.Permissions.Migrations;

/// <summary>
/// One step in changing what a <c>.config.json</c> looks like, e.g. a new required property or a property that moves into an object. It brings
/// a configuration written for the version before it up to <see cref="Version"/>. Steps are listed in <see cref="ConfigMigrations.All"/> and
/// described in docs/permissions.md (Versions and migrations): read it before adding one.
/// </summary>
/// <remarks>
/// A migration is applied to every configuration that is older than <see cref="Version"/>, and to nothing else, so it may assume the structure
/// the previous version had (never that it was already changed). It must not set <c>version</c>: that is done after it.
/// </remarks>
public interface IConfigMigration
{
    /// <summary>The version of the configuration after this migration. Unique, and not newer than <c>Config:Version</c> in appsettings.</summary>
    SemVersion Version { get; }

    /// <summary>What the migration does, in a sentence. Logged when it is applied.</summary>
    string Description { get; }

    /// <summary>
    /// Changes the configuration, which is the root object of the file, in place. Comments are not part of what is changed here: a file that goes
    /// through a migration is written again without them (the layout is the standard one).
    /// </summary>
    /// <exception cref="InvalidOperationException">If the configuration is not what the migration can work with. Nothing is saved then.</exception>
    void Apply(JsonObject root);
}

/// <summary>All migrations there are, oldest first. A new one is a class that implements <see cref="IConfigMigration"/> and goes into this list.</summary>
public static class ConfigMigrations
{
    public static IReadOnlyList<IConfigMigration> All { get; } = [];
}
