using System.Text.Json.Nodes;
using Semver;

namespace Notes.Features.Configuration.Migrations;

/// <summary>
/// 1.5.0: <c>archive</c> comes in, which every folder's configuration has. Existing folders are not archived; they get the block with all its
/// settings at their defaults and its comment, as a new configuration has it. Done on the text, so the file's comments stay.
/// </summary>
public sealed class AddDisabledArchive : ITextConfigMigration
{
    public SemVersion Version => new(1, 5, 0);
    public string Description => "\"archive\": { \"enabled\": false, … } is added before files";

    public string ApplyToText(string text)
        => ConfigText.AddDisabledArchive(text) ?? throw new InvalidOperationException("The text can't be read as a configuration.");

    public void Apply(JsonObject root)
    {
        if (root.ContainsKey(ConfigParser.ArchiveKey) || root.IndexOf(ConfigParser.FilesKey) is not (>= 0 and var files))
            return;

        // Where the text version puts it: before files (without the comment, which an object can't hold)
        root.Insert(files, ConfigParser.ArchiveKey, new JsonObject
        {
            [ConfigParser.ArchiveEnabledKey] = false,
            [ConfigParser.ArchiveFiletypeKey] = "7z",
            [ConfigParser.ArchiveCompressionLevelKey] = 2,
            [ConfigParser.ArchiveEncryptedKey] = false,
            [ConfigParser.ArchiveOnLowestLevelKey] = false
        });
    }
}
