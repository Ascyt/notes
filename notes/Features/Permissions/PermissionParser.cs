using System.Text.Json;
using notes.Features.Helpers;

namespace notes.Features.Permissions;

/// <summary>The outcome of reading a configuration: the value if the text is valid, otherwise what is wrong with it.</summary>
public sealed record ParseResult<T>(T? Value, IReadOnlyList<string> Errors) where T : class
{
    public bool IsValid => Value is not null;

    public static ParseResult<T> Ok(T value) => new(value, []);

    public static ParseResult<T> Failed(IReadOnlyList<string> errors) => new(null, errors);

    /// <summary>The errors as one text, a line each, for the user.</summary>
    public string ErrorText => string.Join('\n', Errors.Select(error => "• " + error));
}

/// <summary>
/// Reads and validates the text of a configuration: JSON in which comments are allowed, and nothing else the schema (docs/permissions.md)
/// doesn't say. It is strict: unknown or repeated properties, a value of the wrong type or a principal that is spelled wrong are errors, so
/// is a principal that is in two of the lists that exclude each other. A problem is never guessed away, as that could grant what nobody meant to.
/// </summary>
public static class PermissionParser
{
    /// <summary>The longest text that is read, in characters. Far more than a folder with thousands of files needs.</summary>
    public const int MaxLength = 1024 * 1024;

    public const string InheritKey = "inherit";
    public const string PermsKey = "perms";
    public const string FilesKey = "files";
    public const string NameKey = "name";

    private static readonly string[] RwKeys = ["rwAllow", "rwReadOnly", "rwDeny"];
    private static readonly string[] CdKeys = ["cdAllow", "cdCreateOnly", "cdDeny"];

    /// <summary>The lists that give the right to change something (write, create, delete), which "everyone" (including those not logged in) can't be in.</summary>
    private static readonly string[] NoAnonymousKeys = ["rwAllow", "cdAllow", "cdCreateOnly"];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        MaxDepth = 16
    };

    /// <summary>
    /// Reads the configuration of a folder: <c>{ "perms": {...}, "files": [ { "name": "...", "perms": {...} } ] }</c>.
    /// </summary>
    /// <param name="existingFiles">
    /// The names of the files in the folder. If given, every entry of <c>files</c> must be one of them (this is what saving checks). The
    /// permissions that are in force are read without it: a file that was removed from outside doesn't make its folder unusable.
    /// </param>
    public static ParseResult<FolderConfig> ParseFolder(string text, IReadOnlySet<string>? existingFiles)
    {
        List<string> errors = [];
        using JsonDocument? document = Open(text, errors);
        if (document is null)
            return ParseResult<FolderConfig>.Failed(errors);

        Dictionary<string, JsonElement>? root = ReadObject(document.RootElement, "", [PermsKey, FilesKey], required: [PermsKey, FilesKey], errors);
        if (root is null)
            return ParseResult<FolderConfig>.Failed(errors);

        PermsBlock? perms = ParsePerms(root[PermsKey], PermsKey, errors);
        List<FileEntry> files = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        if (root[FilesKey].ValueKind != JsonValueKind.Array)
            errors.Add($"{FilesKey}: must be an array.");
        else
        {
            int index = 0;
            foreach (JsonElement element in root[FilesKey].EnumerateArray())
            {
                string path = $"{FilesKey}[{index++}]";
                Dictionary<string, JsonElement>? entry = ReadObject(element, path, [NameKey, PermsKey], required: [NameKey, PermsKey], errors);
                if (entry is null)
                    continue;

                string? name = ParseName(entry[NameKey], path, names, existingFiles, errors);
                PermsBlock? entryPerms = ParsePerms(entry[PermsKey], path + "." + PermsKey, errors);
                if (name is not null && entryPerms is not null)
                    files.Add(new FileEntry(name, entryPerms));
            }
        }

        return errors.Count > 0 || perms is null
            ? ParseResult<FolderConfig>.Failed(errors)
            : ParseResult<FolderConfig>.Ok(new FolderConfig(perms, files));
    }

    /// <summary>
    /// Reads what the editor shows for a single file: <c>{ "perms": {...} }</c>, the entry of the file without its name (which is the file's own).
    /// </summary>
    public static ParseResult<PermsBlock> ParseFileBody(string text)
    {
        List<string> errors = [];
        using JsonDocument? document = Open(text, errors);
        if (document is null)
            return ParseResult<PermsBlock>.Failed(errors);

        Dictionary<string, JsonElement>? root = ReadObject(document.RootElement, "", [PermsKey], required: [PermsKey], errors);
        PermsBlock? perms = root is null ? null : ParsePerms(root[PermsKey], PermsKey, errors);

        return errors.Count > 0 || perms is null
            ? ParseResult<PermsBlock>.Failed(errors)
            : ParseResult<PermsBlock>.Ok(perms);
    }

    private static JsonDocument? Open(string text, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add("The configuration is empty.");
            return null;
        }

        if (text.Length > MaxLength)
        {
            errors.Add("The configuration is too long.");
            return null;
        }

        try
        {
            return JsonDocument.Parse(text, DocumentOptions);
        }
        catch (JsonException e)
        {
            // The message is meant for programmers: it says where on its own, and tells to change options that are not the user's to change
            string message = e.Message.Split(" LineNumber:")[0].Split(" Path:")[0].Replace(" Change the reader options.", "").Replace(", when isFinalBlock is true", "");
            errors.Add(e.LineNumber is { } line
                ? $"Not valid JSON (line {line + 1}, column {(e.BytePositionInLine ?? 0) + 1}): {message}"
                : "Not valid JSON: " + message);
            return null;
        }
    }

    /// <summary>
    /// The properties of the object by name. Reports (and returns null for) anything but an object, and a property that is not allowed, is
    /// there twice or is missing although required. Comments between the properties are not part of the value, they are skipped.
    /// </summary>
    private static Dictionary<string, JsonElement>? ReadObject(JsonElement element, string path, string[] allowed, string[] required, List<string> errors)
    {
        string where = path == "" ? "" : path + ": ";
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(where + "must be an object.");
            return null;
        }

        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        bool valid = true;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string propertyPath = path == "" ? property.Name : path + "." + property.Name;
            if (!allowed.Contains(property.Name))
            {
                errors.Add($"{propertyPath}: unknown property. Allowed here: {string.Join(", ", allowed)}.");
                valid = false;
            }
            else if (!properties.TryAdd(property.Name, property.Value))
            {
                errors.Add($"{propertyPath}: is there more than once.");
                valid = false;
            }
        }

        foreach (string key in required.Where(key => !properties.ContainsKey(key)))
        {
            errors.Add($"{(path == "" ? key : path + "." + key)}: is required.");
            valid = false;
        }

        return valid ? properties : null;
    }

    private static string? ParseName(JsonElement element, string path, HashSet<string> names, IReadOnlySet<string>? existingFiles, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } name)
        {
            errors.Add($"{path}.{NameKey}: must be a string.");
            return null;
        }

        if (!PathService.IsValid(name))
        {
            errors.Add($"{path}.{NameKey}: '{name}' is not a name a file can have.");
            return null;
        }

        if (!names.Add(name))
        {
            errors.Add($"{path}.{NameKey}: '{name}' is there more than once.");
            return null;
        }

        if (existingFiles is not null && !existingFiles.Contains(name))
        {
            errors.Add($"{path}.{NameKey}: there is no file '{name}' in this folder.");
            return null;
        }

        return name;
    }

    /// <summary>Reads a <c>perms</c> block. Everything but <c>inherit</c> is optional, and a list that is missing is an empty one.</summary>
    private static PermsBlock? ParsePerms(JsonElement element, string path, List<string> errors)
    {
        string[] keys = [InheritKey, .. RwKeys, .. CdKeys];
        Dictionary<string, JsonElement>? properties = ReadObject(element, path, keys, required: [InheritKey], errors);
        if (properties is null)
            return null;

        bool valid = true;
        bool inherit = false;
        if (properties[InheritKey].ValueKind is JsonValueKind.True or JsonValueKind.False)
            inherit = properties[InheritKey].GetBoolean();
        else
        {
            errors.Add($"{path}.{InheritKey}: must be true or false.");
            valid = false;
        }

        // A principal can be in one list of each pair of levels, not in several: what is allowed and denied can't both be meant.
        // (It may be in an rw list and a cd list: they are about different things.)
        Dictionary<string, HashSet<string>> lists = [];
        foreach (string key in keys.Skip(1))
        {
            HashSet<string>? list = ParseList(properties.GetValueOrDefault(key), $"{path}.{key}", errors);
            valid &= list is not null;
            lists[key] = list ?? [];
        }

        valid &= CheckExclusive(lists, RwKeys, path, errors);
        valid &= CheckExclusive(lists, CdKeys, path, errors);

        // Someone who is not logged in must never be able to change the notes: they may be allowed to read, or denied, and that is all
        foreach (string key in NoAnonymousKeys.Where(key => lists[key].Contains(Principal.Everyone)))
        {
            errors.Add($"{path}.{key}: '{Principal.Everyone}' (all users, including those who are not logged in) can't be in this list, as they may not change anything. Use '{Principal.LoggedIn}' for all who are logged in. Only rwReadOnly, rwDeny and cdDeny allow '{Principal.Everyone}'.");
            valid = false;
        }

        return valid
            ? new PermsBlock(inherit, lists["rwAllow"], lists["rwReadOnly"], lists["rwDeny"], lists["cdAllow"], lists["cdCreateOnly"], lists["cdDeny"])
            : null;
    }

    private static HashSet<string>? ParseList(JsonElement element, string path, List<string> errors)
    {
        HashSet<string> list = new(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Undefined)
            return list;

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{path}: must be an array of principals.");
            return null;
        }

        bool valid = true;
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string itemPath = $"{path}[{index++}]";
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{itemPath}: must be a string.");
                valid = false;
            }
            else if (Principal.Normalize(item.GetString()!, out string? error) is not { } principal)
            {
                errors.Add($"{itemPath}: {error}");
                valid = false;
            }
            else if (!list.Add(principal))
            {
                errors.Add($"{itemPath}: '{item.GetString()}' is in this list more than once.");
                valid = false;
            }
        }

        return valid ? list : null;
    }

    private static bool CheckExclusive(Dictionary<string, HashSet<string>> lists, string[] keys, string path, List<string> errors)
    {
        bool valid = true;
        for (int i = 0; i < keys.Length; i++)
        {
            for (int j = i + 1; j < keys.Length; j++)
            {
                foreach (string principal in lists[keys[i]].Intersect(lists[keys[j]], StringComparer.Ordinal))
                {
                    errors.Add($"{path}: '{principal}' is in both {keys[i]} and {keys[j]}, which exclude each other.");
                    valid = false;
                }
            }
        }

        return valid;
    }
}
