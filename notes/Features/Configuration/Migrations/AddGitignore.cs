using System.Text.Json.Nodes;
using Semver;

namespace notes.Features.Configuration.Migrations;

/// <summary>1.1.0: the history of the notes came in, and with it <c>gitignore</c>, which every configuration has. Existing folders stay in the history.</summary>
public sealed class AddGitignore : IConfigMigration
{
    public SemVersion Version => new(1, 1, 0);
    public string Description => "gitignore: false is added after perms";

    public void Apply(JsonObject root)
    {
        if (root.ContainsKey(ConfigParser.GitignoreKey))
            throw new InvalidOperationException($"There is a {ConfigParser.GitignoreKey} already, which the version before didn't have.");

        // Where the default configuration has it: after perms (at the end if there is no perms, which the parser then reports)
        int perms = root.IndexOf(ConfigParser.PermsKey);
        root.Insert(perms < 0 ? root.Count : perms + 1, ConfigParser.GitignoreKey, false);
    }
}
