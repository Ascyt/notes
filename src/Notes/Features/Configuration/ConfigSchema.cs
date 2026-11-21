using System.Text.Json;

namespace Notes.Features.Configuration;

/// <summary>
/// The schema of the configuration as JSON Schema, which the editor uses to complete, explain and check what an admin types. The server
/// doesn't depend on it: <see cref="ConfigParser"/> is what decides (and what it accepts is what this describes).
/// </summary>
/// <remarks>What each setting means is in its texts (<see cref="ConfigTexts"/>), the site's to replace.</remarks>
public sealed class ConfigSchema(ConfigTexts texts)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private string? _folder;
    private string? _fileBody;

    /// <summary>The schema of a folder's configuration, as JSON.</summary>
    public string Folder => _folder ??= JsonSerializer.Serialize(Build(forFile: false), Options);

    /// <summary>The schema of what the editor of a single file shows (its entry without the name), as JSON.</summary>
    public string FileBody => _fileBody ??= JsonSerializer.Serialize(Build(forFile: true), Options);

    private Dictionary<string, object> Build(bool forFile)
    {
        Dictionary<string, object> properties = new()
        {
            ["perms"] = new Dictionary<string, object>
            {
                ["$ref"] = "#/definitions/perms",
                ["description"] = texts.SchemaPerms(forFile)
            }
        };

        if (!forFile)
        {
            properties["version"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["pattern"] = @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$",
                ["description"] = texts.SchemaVersion,
                ["examples"] = new[] { "1.2.0" }
            };
            properties["gitignore"] = new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = texts.SchemaGitignore
            };
            properties["files"] = new Dictionary<string, object>
            {
                ["description"] = texts.SchemaFiles,
                ["type"] = "array",
                ["items"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new[] { "name", "perms" },
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = texts.SchemaFileName },
                        ["perms"] = new Dictionary<string, object> { ["$ref"] = "#/definitions/perms",
                            ["description"] = texts.SchemaPerms(isFile: true) }
                    }
                }
            };
        }

        return new Dictionary<string, object>
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = forFile ? new[] { "perms" } : new[] { "version", "perms", "gitignore", "files" },
            ["properties"] = properties,
            ["definitions"] = new Dictionary<string, object>
            {
                ["perms"] = Perms(),
                ["principals"] = PrincipalList(@"^([al]|[ugr]:\S(.*\S)?)$",
                    texts.SchemaPrincipals, ["u:alice", "g:team-a",
                    "r:viewer", "l", "a"]),
                // The lists that allow changing something: no "a", those who are not logged in may not change anything
                ["changingPrincipals"] = PrincipalList(@"^(l|[ugr]:\S(.*\S)?)$", texts.SchemaChangingPrincipals, ["u:alice", "g:team-a", "r:editor", "l"])
            }
        };
    }

    private static Dictionary<string, object> PrincipalList(string pattern, string description, string[] examples)
        => new()
        {
            ["type"] = "array",
            ["uniqueItems"] = true,
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["pattern"] = pattern,
                ["description"] = description,
                ["examples"] = examples
            }
        };

    private Dictionary<string, object> Perms()
    {
        Dictionary<string, object> properties = new()
        {
            ["inherit"] = new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = texts.SchemaInherit
            }
        };

        string[] lists = ["rwAllow", "rwReadOnly", "rwDeny", "cdAllow", "cdCreateOnly", "cdDeny", "historyAllow", "historyExistingOnly", "historyDeny"];
        foreach (string key in lists)
        {
            string definition = key is "rwAllow" or "cdAllow" or "cdCreateOnly" ? "changingPrincipals" : "principals";
            properties[key] = new Dictionary<string, object>
            {
                ["$ref"] = "#/definitions/" + definition,
                ["description"] = texts.SchemaList(key) + " " + texts.SchemaOneListOfEach
            };
        }

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "inherit" },
            ["properties"] = properties
        };
    }
}
