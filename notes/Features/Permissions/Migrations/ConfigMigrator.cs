using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Semver;

namespace notes.Features.Permissions.Migrations;

/// <summary>What <see cref="ConfigMigrator.Migrate"/> made of a configuration's text.</summary>
/// <param name="Text">The text up to date, or as it was if there was nothing to do or something went wrong.</param>
/// <param name="From">
/// The version the text had; <see cref="ConfigVersions.Zero"/> if it has none (a file from before there were versions), null if that can't be told
/// (it is not valid JSON, not an object, or the version isn't spelled right), which is for <see cref="PermissionParser"/> to report.
/// </param>
/// <param name="Changed">Whether <paramref name="Text"/> differs from what was given.</param>
/// <param name="Error">Why the text can't be used, if it can't: it is from a newer version of the app, or a migration failed.</param>
public sealed record MigrationResult(string Text, SemVersion? From, bool Changed, string? Error);

/// <summary>
/// Brings the text of a <c>.config.json</c> up to the current version of the app (<c>Config:Version</c> in appsettings): every migration newer
/// than the version the text has, up to the current one, is applied in order, and the text gets the current version. Not every version has a
/// migration: if none lies between, only the version is set, in the text as it is (comments and layout stay). Migrations are the steps of
/// <see cref="ConfigMigrations.All"/>; what writing one involves is described in docs/permissions.md.
/// </summary>
public sealed class ConfigMigrator
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = false, MaxDepth = 16 };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IReadOnlyList<IConfigMigration> _migrations;
    private readonly ILogger<ConfigMigrator> _logger;

    /// <param name="current">The version of the app, which is the version the files are written with.</param>
    /// <param name="migrations">The steps, in any order.</param>
    /// <exception cref="InvalidOperationException">
    /// If the steps don't fit the current version: two lead to the same version, or one to a version after the current one (so that the version
    /// in appsettings was not raised, or raised too little). The app doesn't start then, rather than read configurations it can't bring up to date.
    /// </exception>
    public ConfigMigrator(SemVersion current, IEnumerable<IConfigMigration> migrations, ILogger<ConfigMigrator> logger)
    {
        Current = current;
        _logger = logger;
        _migrations = [.. migrations.OrderBy(migration => migration.Version, SemVersion.PrecedenceComparer)];

        foreach (IConfigMigration migration in _migrations)
        {
            if (migration.Version.IsPrerelease || migration.Version.Metadata.Length > 0)
                throw new InvalidOperationException($"The configuration migration '{migration.Description}' leads to version {migration.Version}, which is not major.minor.patch.");
            if (migration.Version.ComparePrecedenceTo(ConfigVersions.Zero) <= 0)
                throw new InvalidOperationException($"The configuration migration '{migration.Description}' leads to version {migration.Version}, but versions start after {ConfigVersions.Zero}.");
            if (migration.Version.ComparePrecedenceTo(current) > 0)
                throw new InvalidOperationException($"The configuration migration '{migration.Description}' leads to version {migration.Version}, which is after the current version {current}. Raise Config:Version in appsettings.");
        }

        for (int i = 1; i < _migrations.Count; i++)
        {
            if (_migrations[i - 1].Version == _migrations[i].Version)
                throw new InvalidOperationException($"Two configuration migrations lead to version {_migrations[i].Version}: '{_migrations[i - 1].Description}' and '{_migrations[i].Description}'.");
        }
    }

    /// <summary>The version of the app, which is the version the files are written with.</summary>
    public SemVersion Current { get; }

    /// <summary>
    /// Brings the text up to date. A text that is not readable as a configuration at all (not JSON, no object) is returned as it is, as the
    /// parser reports that better.
    /// </summary>
    public MigrationResult Migrate(string text)
    {
        if (!TryReadVersion(text, out SemVersion? from))
            return new MigrationResult(text, null, Changed: false, Error: null);

        SemVersion version = from ?? ConfigVersions.Zero;
        if (version == Current)
            return new MigrationResult(text, version, Changed: false, Error: null);

        if (version.ComparePrecedenceTo(Current) > 0)
            return new MigrationResult(text, version, Changed: false, Error: $"version: {version} is newer than the {Current} that this app reads, so it can't be used. Was it written by a newer version of the app?");

        IConfigMigration[] pending = [.. _migrations.Where(migration => migration.Version.ComparePrecedenceTo(version) > 0)];

        // Without a step to apply only the version is written, into the text as it is, so that its comments and layout stay
        (string? migrated, string? failure) = pending.Length == 0 ? (ConfigText.SetVersion(text, Current), null) : Run(text, pending);
        return migrated is null
            ? new MigrationResult(text, version, Changed: false, Error: failure ?? "version: can't be brought up to date, as it is not a valid configuration.")
            : new MigrationResult(migrated, version, Changed: migrated != text, Error: null);
    }

    private (string? Text, string? Failure) Run(string text, IConfigMigration[] pending)
    {
        IConfigMigration? running = null;
        try
        {
            if (JsonNode.Parse(text, documentOptions: DocumentOptions) is not JsonObject root)
                return (null, null);

            foreach (IConfigMigration migration in pending)
            {
                running = migration;
                _logger.LogInformation("Migrating a configuration to {version}: {description}", migration.Version, migration.Description);
                migration.Apply(root);
                SetVersion(root, migration.Version);
            }

            // The last step may be older than the current version: the format didn't change in between
            SetVersion(root, Current);
            return (root.ToJsonString(WriteOptions) + "\n", null);
        }
        catch (Exception e)
        {
            // Whatever a migration trips over, the configuration is then not usable (and left as it is), but the app goes on
            _logger.LogError(e, "The configuration migration to {version} failed", running?.Version);
            return (null, $"version: could not be migrated to {running?.Version.ToString() ?? "the current version"}: {e.Message}");
        }
    }

    /// <summary>Sets <c>version</c>, which stays (or becomes) the first property.</summary>
    private static void SetVersion(JsonObject root, SemVersion version)
    {
        _ = root.Remove(PermissionParser.VersionKey);
        root.Insert(0, PermissionParser.VersionKey, version.ToString());
    }

    /// <summary>
    /// The version the text says it has: <paramref name="version"/> is null if it says none. False if the text isn't an object, or what it
    /// says isn't a version.
    /// </summary>
    private static bool TryReadVersion(string text, out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > PermissionParser.MaxLength)
            return false;

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(text, DocumentOptions);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            JsonElement found = default;
            foreach (JsonProperty property in parsed.RootElement.EnumerateObject().Where(property => property.Name == PermissionParser.VersionKey))
                found = property.Value;

            if (found.ValueKind == JsonValueKind.Undefined)
                return true;

            if (found.ValueKind != JsonValueKind.String || !ConfigVersions.TryParse(found.GetString(), out SemVersion? parsedVersion))
                return false;

            version = parsedVersion;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
