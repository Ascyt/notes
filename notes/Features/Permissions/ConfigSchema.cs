using System.Text.Json;

namespace notes.Features.Permissions;

/// <summary>
/// The schema of the configuration as JSON Schema, which the editor uses to complete, explain and check what an admin types. The server
/// doesn't depend on it: <see cref="PermissionParser"/> is what decides (and what it accepts is what this describes).
/// </summary>
public static class ConfigSchema
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>The schema of a folder's configuration, as JSON.</summary>
    public static string Folder { get; } = JsonSerializer.Serialize(Build(forFile: false), Options);

    /// <summary>The schema of what the editor of a single file shows (its entry without the name), as JSON.</summary>
    public static string FileBody { get; } = JsonSerializer.Serialize(Build(forFile: true), Options);

    private static Dictionary<string, object> Build(bool forFile)
    {
        Dictionary<string, object> properties = new()
        {
            ["perms"] = new Dictionary<string, object>
            {
                ["$ref"] = "#/definitions/perms",
                ["description"] = forFile
                    ? "The permissions of this file."
                    : "The permissions of this folder. Its files and subfolders that inherit get these permissions."
            }
        };

        if (!forFile)
        {
            properties["files"] = new Dictionary<string, object>
            {
                ["description"] = "The files of this folder, which the app keeps up to date. A file without an entry has the permissions of the folder.",
                ["type"] = "array",
                ["items"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new[] { "name", "perms" },
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The name of the file." },
                        ["perms"] = new Dictionary<string, object> { ["$ref"] = "#/definitions/perms", ["description"] = "The permissions of this file." }
                    }
                }
            };
        }

        return new Dictionary<string, object>
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = forFile ? new[] { "perms" } : new[] { "perms", "files" },
            ["properties"] = properties,
            ["definitions"] = new Dictionary<string, object>
            {
                ["perms"] = Perms(),
                ["principals"] = PrincipalList(@"^([al]|[ugr]:\S(.*\S)?)$", "u:<user name>, g:<group>, r:<role>, a for all users or l for all users who are logged in.", ["u:alice", "g:team-a", "r:viewer", "l", "a"]),
                // The lists that allow changing something: no "a", those who are not logged in may not change anything
                ["changingPrincipals"] = PrincipalList(@"^(l|[ugr]:\S(.*\S)?)$", "u:<user name>, g:<group>, r:<role> or l for all users who are logged in. Not a: those who are not logged in may not change anything.", ["u:alice", "g:team-a", "r:editor", "l"])
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

    private static Dictionary<string, object> Perms()
    {
        Dictionary<string, object> properties = new()
        {
            ["inherit"] = new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = "Whether those that none of the lists below mention get what the folder above (for a file: its folder) gives them. If false, they get nothing. Only about where this entry's own permissions come from: what is inside a folder still gets the folder's permissions if it inherits itself."
            }
        };

        (string Key, string Description)[] lists =
        [
            ("rwAllow", "May open (read) and change (write) the contents."),
            ("rwReadOnly", "May open (read) the contents, but not change them."),
            ("rwDeny", "May not open the contents. Beats rwAllow and rwReadOnly."),
            ("cdAllow", "May create entries here, and delete (also move, rename) this one."),
            ("cdCreateOnly", "May create entries here, but not delete."),
            ("cdDeny", "May neither create nor delete. Beats cdAllow and cdCreateOnly.")
        ];
        foreach ((string key, string description) in lists)
        {
            string definition = key is "rwAllow" or "cdAllow" or "cdCreateOnly" ? "changingPrincipals" : "principals";
            properties[key] = new Dictionary<string, object> { ["$ref"] = "#/definitions/" + definition, ["description"] = description + " Each principal may be in one of the rw lists and one of the cd lists." };
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
