using System.Diagnostics.CodeAnalysis;
using Semver;

namespace Notes.Features.Configuration.Migrations;

/// <summary>
/// How the version of the configuration format is written, in every <c>.config.json</c>: <c>major.minor.patch</c> as <see cref="SemVersion"/>
/// (the Semver library) reads it strictly, and nothing more (no pre-release or build part). Compare versions with
/// <see cref="SemVersion.ComparePrecedenceTo"/>.
/// </summary>
public static class ConfigVersions
{
    /// <summary>
    /// The version of the format this app writes. Every <c>.config.json</c> is brought up to it when it is read, which sets its <c>version</c>
    /// and applies the migrations of <see cref="ConfigMigrations.All"/> after the version it had. It is raised with every change of the format
    /// (docs/development/config-migrations.md); a version that changes nothing in the files needs no migration.
    /// </summary>
    public static SemVersion Current { get; } = new(1, 5, 0);

    /// <summary>What a configuration that has no version is: written before there were versions.</summary>
    public static SemVersion Zero { get; } = new(0, 0, 0);

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        if (SemVersion.TryParse(text, SemVersionStyles.Strict, out version) && !version.IsPrerelease && version.Metadata.Length == 0)
            return true;

        version = null;
        return false;
    }

    /// <exception cref="FormatException">If the text is not <c>major.minor.patch</c>.</exception>
    public static SemVersion Parse(string? text)
        => TryParse(text, out SemVersion? version)
            ? version
            : throw new FormatException($"'{text}' is not a version. Write it as major.minor.patch, e.g. 1.0.0.");
}
