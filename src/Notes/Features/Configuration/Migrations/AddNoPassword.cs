using System.Text.Json.Nodes;
using Semver;

namespace Notes.Features.Configuration.Migrations;

/// <summary>
/// 1.4.0: a folder's configuration says <c>"password": null</c>, with a comment on how one is set, as a new one does. A folder that has a
/// password (or its hash) keeps it. Done on the text, so the file's comments stay.
/// </summary>
public sealed class AddNoPassword : ITextConfigMigration
{
    public SemVersion Version => new(1, 4, 0);
    public string Description => "\"password\": null is added before files";

    public string ApplyToText(string text)
        => ConfigText.AddNoPassword(text) ?? throw new InvalidOperationException("The text can't be read as a configuration.");

    public void Apply(JsonObject root)
    {
        if (root.ContainsKey(ConfigParser.PasswordKey) || root.ContainsKey(ConfigParser.PasswordHashKey) || root.IndexOf(ConfigParser.FilesKey) is not (>= 0 and var files))
            return;

        // Where the text version puts it: before files (without the comment, which an object can't hold)
        root.Insert(files, ConfigParser.PasswordKey, null);
    }
}
