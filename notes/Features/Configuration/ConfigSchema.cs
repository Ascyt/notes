using System.Text.Json;

namespace notes.Features.Configuration;

/// <summary>
/// The schema of the configuration as JSON Schema, which the editor uses to complete, explain and check what an admin types. The server
/// doesn't depend on it: <see cref="ConfigParser"/> is what decides (and what it accepts is what this describes).
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
            properties["version"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["pattern"] = @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$",
                ["description"] = "The version of the format of this file (major.minor.patch). The app keeps it up to date and brings older files up to the current version when it reads them: don't change it.",
                ["examples"] = new[] { "1.2.0" }
            };
            properties["gitignore"] = new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = "Whether the history of the notes leaves out the files of this folder and of its subfolders (a subfolder with its own configuration decides for itself). Configuration files are always kept."
            };
            properties["files"] = new Dictionary<string, object>
            {
                ["description"] = "The files of this folder, which the app keeps up to date. A file without an entry has the settings of the folder.",
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
            ["required"] = forFile ? new[] { "perms" } : new[] { "version", "perms", "gitignore", "files" },
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
            ("cdDeny", "May neither create nor delete. Beats cdAllow and cdCreateOnly."),
            ("historyAllow", "May see earlier states (read-only), also of what was deleted, moved or renamed since. Needs read access (rw) and this, now and at that time."),
            ("historyExistingOnly", "May see earlier states (read-only) of what exists now, not of what was deleted, moved or renamed since. Needs read access (rw) and this, now and at that time."),
            ("historyDeny", "May not see earlier states, whatever was given at that time. Beats historyAllow and historyExistingOnly.")
        ];
        foreach ((string key, string description) in lists)
        {
            string definition = key is "rwAllow" or "cdAllow" or "cdCreateOnly" ? "changingPrincipals" : "principals";
            properties[key] = new Dictionary<string, object> { ["$ref"] = "#/definitions/" + definition, ["description"] = description + " Each principal may be in one of the rw lists, one of the cd lists and one of the history lists." };
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
