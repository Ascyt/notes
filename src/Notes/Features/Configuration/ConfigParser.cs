using System.Text.Json;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration.Migrations;
using Notes.Features.Helpers;
using Notes.Features.Passwords;
using Notes.Features.Permissions;

namespace Notes.Features.Configuration;

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
/// Reads and validates the text of a configuration: JSON in which comments are allowed, and nothing else the schema
/// (docs/administration/configuration.md)
/// doesn't say. It is strict: unknown or repeated properties, a value of the wrong type or a principal that is spelled wrong are errors, so
/// is a principal that is in two of the lists that exclude each other. A problem is never guessed away, as that could grant what nobody meant to.
/// </summary>
public static class ConfigParser
{
    /// <summary>The longest text that is read, in characters. Far more than a folder with thousands of files needs.</summary>
    public const int MaxLength = 1024 * 1024;

    public const string VersionKey = "version";
    public const string InheritKey = "inherit";
    public const string PermsKey = "perms";
    public const string GitignoreKey = "gitignore";
    public const string FilesKey = "files";
    public const string NameKey = "name";

    /// <summary>A password as typed into the editor, which saving replaces with its hash (<see cref="PasswordHashKey"/>).</summary>
    public const string PasswordKey = "password";
    public const string PasswordHashKey = "passwordHash";

    public const string ArchiveKey = "archive";
    public const string ArchiveEnabledKey = "enabled";
    public const string ArchiveFiletypeKey = "filetype";
    public const string ArchiveCompressionLevelKey = "compressionLevel";
    public const string ArchiveEncryptedKey = "encrypted";
    public const string ArchiveOnLowestLevelKey = "onLowestLevel";

    /// <summary>The highest <c>compressionLevel</c>; 0 is none (a 7z, which can't store, the least).</summary>
    public const int MaxCompressionLevel = 3;

    private static readonly string[] RwKeys = ["rwAllow", "rwReadOnly", "rwDeny"];
    private static readonly string[] CdKeys = ["cdAllow", "cdCreateOnly", "cdDeny"];
    private static readonly string[] HistoryKeys = ["historyAllow", "historyExistingOnly", "historyDeny"];

    /// <summary>
    /// The lists that give the right to change something (write, create, delete), which "everyone" (including those not logged in) can't be in.
    /// </summary>
    private static readonly string[] NoAnonymousKeys = ["rwAllow", "cdAllow", "cdCreateOnly"];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        MaxDepth = 16
    };

    /// <summary>
    /// Reads the configuration of a folder: <c>{ "version": "...", "perms": {...}, "gitignore": false, "files": [ { "name": "...", "perms":
    /// {...} } ] }</c>.
    /// The version only has to be spelled right here: that it is the current one is for <see cref="Migrations.ConfigMigrator"/> to see to,
    /// before the text is read.
    /// </summary>
    /// <param name="existingFiles">
    /// The names of the files in the folder. If given, every entry of <c>files</c> must be one of them (this is what saving checks). The
    /// permissions that are in force are read without it: a file that was removed from outside doesn't make its folder unusable.
    /// </param>
    /// <param name="texts">How the errors are worded: the site's, where they are shown; the app's own (<see cref="ConfigTexts.Default"/>) without.</param>
    /// <param name="allowsPlainPasswords">
    /// Whether a <c>password</c> may be there (as typed into the editor, which saving replaces with its hash). In a file that was saved it is
    /// an error: it was written there by hand, and only its hash may be kept.
    /// </param>
    public static ParseResult<FolderConfig> ParseFolder(string text, IReadOnlySet<string>? existingFiles, ConfigTexts? texts = null,
        bool allowsPlainPasswords = false)
    {
        Problems errors = new(texts ?? ConfigTexts.Default);
        using JsonDocument? document = Open(text, errors);
        if (document is null)
            return ParseResult<FolderConfig>.Failed(errors);

        Dictionary<string, JsonElement>? root = ReadObject(document.RootElement, "",
            [VersionKey, PermsKey, GitignoreKey, PasswordKey, PasswordHashKey, ArchiveKey, FilesKey],
            required: [PermsKey, GitignoreKey, FilesKey], errors);
        if (root is null)
            return ParseResult<FolderConfig>.Failed(errors);

        if (root.TryGetValue(VersionKey, out JsonElement version) && (version.ValueKind != JsonValueKind.String
            || !ConfigVersions.TryParse(version.GetString(), out _)))
            errors.Add(errors.Texts.NotAVersion(VersionKey));

        bool gitignore = root[GitignoreKey].ValueKind == JsonValueKind.True;
        if (root[GitignoreKey].ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            errors.Add(errors.Texts.NotABoolean(GitignoreKey));

        PermsBlock? perms = ParsePerms(root[PermsKey], PermsKey, errors);
        string? passwordHash = ParsePassword(root, "", allowsPlainPasswords, errors);
        // Optional, so that a file without it (written by hand) is still valid; every file the app writes has it (principle 19). Null (or none)
        // is not set: an "onLowestLevel" folder above may archive this one (ArchiveSettingsResolver)
        bool archiveIsSet = root.TryGetValue(ArchiveKey, out JsonElement archiveElement) && archiveElement.ValueKind != JsonValueKind.Null;
        ArchiveSettings? archive = archiveIsSet ? ParseArchive(archiveElement, errors) : ArchiveSettings.Disabled;
        // An archive is encrypted with the folder's password: without one there is nothing to encrypt it with (a password only being typed
        // counts: its hash is made when it is saved)
        bool hasPassword = passwordHash is not null
                           || (root.TryGetValue(PasswordKey, out JsonElement plain) && plain.ValueKind == JsonValueKind.String);
        if (archive is { Enabled: true, Encrypted: true } && !hasPassword)
            errors.Add(errors.Texts.EncryptedNeedsPassword(ArchiveKey + "." + ArchiveEncryptedKey, PasswordKey));
        List<FileEntry> files = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        if (root[FilesKey].ValueKind != JsonValueKind.Array)
            errors.Add(errors.Texts.NotAnArray(FilesKey));
        else
        {
            int index = 0;
            foreach (JsonElement element in root[FilesKey].EnumerateArray())
            {
                string path = $"{FilesKey}[{index++}]";
                Dictionary<string, JsonElement>? entry = ReadObject(element, path, [NameKey, PermsKey, PasswordKey, PasswordHashKey],
                    required: [NameKey, PermsKey], errors);
                if (entry is null)
                    continue;

                string? name = ParseName(entry[NameKey], path, names, existingFiles, errors);
                PermsBlock? entryPerms = ParsePerms(entry[PermsKey], path + "." + PermsKey, errors);
                string? entryPasswordHash = ParsePassword(entry, path, allowsPlainPasswords, errors);
                if (name is not null && entryPerms is not null)
                    files.Add(new FileEntry(name, entryPerms, entryPasswordHash));
            }
        }

        return errors.Count > 0 || perms is null || archive is null
            ? ParseResult<FolderConfig>.Failed(errors)
            : ParseResult<FolderConfig>.Ok(new FolderConfig(perms, gitignore, files, passwordHash, archiveIsSet ? archive : null));
    }

    /// <summary>
    /// Reads the <c>archive</c> block: <c>enabled</c> is required, the others have the defaults of <see cref="ArchiveSettings.Disabled"/>.
    /// </summary>
    private static ArchiveSettings? ParseArchive(JsonElement element, Problems errors)
    {
        Dictionary<string, JsonElement>? properties = ReadObject(element, ArchiveKey,
            [ArchiveEnabledKey, ArchiveFiletypeKey, ArchiveCompressionLevelKey, ArchiveEncryptedKey, ArchiveOnLowestLevelKey],
            required: [ArchiveEnabledKey], errors);
        if (properties is null)
            return null;

        string Where(string key) => ArchiveKey + "." + key;
        int before = errors.Count;
        bool? BooleanOf(string key, bool fallback)
        {
            if (!properties.TryGetValue(key, out JsonElement value))
                return fallback;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
            errors.Add(errors.Texts.NotABoolean(Where(key)));
            return null;
        }

        bool? enabled = BooleanOf(ArchiveEnabledKey, false);
        bool? encrypted = BooleanOf(ArchiveEncryptedKey, false);
        bool? onLowestLevel = BooleanOf(ArchiveOnLowestLevelKey, false);

        ArchiveFormat format = ArchiveSettings.Disabled.Format;
        if (properties.TryGetValue(ArchiveFiletypeKey, out JsonElement filetype))
        {
            if (filetype.ValueKind == JsonValueKind.String && filetype.GetString() is "7z" or "zip")
                format = filetype.GetString() == "7z" ? ArchiveFormat.SevenZip : ArchiveFormat.Zip;
            else
                errors.Add(errors.Texts.NotAFiletype(Where(ArchiveFiletypeKey)));
        }

        int level = 2;
        if (properties.TryGetValue(ArchiveCompressionLevelKey, out JsonElement levelElement)
            && !(levelElement.ValueKind == JsonValueKind.Number && levelElement.TryGetInt32(out level) && level is >= 0 and <= MaxCompressionLevel))
            errors.Add(errors.Texts.NotACompressionLevel(Where(ArchiveCompressionLevelKey), MaxCompressionLevel));

        if (errors.Count > before)
            return null;

        ArchiveCompression compression = level switch
        {
            0 => format == ArchiveFormat.SevenZip ? ArchiveCompression.Min : ArchiveCompression.Store,
            1 => ArchiveCompression.Medium,
            2 => ArchiveCompression.High,
            _ => ArchiveCompression.Max
        };
        return new ArchiveSettings(enabled!.Value, format, compression, encrypted!.Value, onLowestLevel!.Value);
    }

    /// <summary>
    /// Checks the <c>password</c> or <c>passwordHash</c> of an object (the root, a file's entry) and returns the hash, or null if there is none
    /// (also for a password, whose hash is only made when it is saved). A password of null is none, as if it weren't there.
    /// </summary>
    private static string? ParsePassword(Dictionary<string, JsonElement> properties, string path, bool allowsPlainPasswords, Problems errors)
    {
        string Where(string key) => path == "" ? key : path + "." + key;
        bool hasPassword = properties.TryGetValue(PasswordKey, out JsonElement password) && password.ValueKind != JsonValueKind.Null;
        bool hasHash = properties.TryGetValue(PasswordHashKey, out JsonElement hash);

        if (hasPassword && hasHash)
        {
            errors.Add(errors.Texts.PasswordAndHash(Where(PasswordKey), Where(PasswordHashKey)));
            return null;
        }

        if (hasPassword)
        {
            if (password.ValueKind != JsonValueKind.String)
                errors.Add(errors.Texts.NotAString(Where(PasswordKey)));
            else if (!allowsPlainPasswords)
                errors.Add(errors.Texts.PasswordNotSaved(Where(PasswordKey), Where(PasswordHashKey)));
            else if (PasswordHashing.Check(password.GetString()!) is not PasswordProblem.None and var problem)
                errors.Add(errors.Texts.NotAPassword(Where(PasswordKey), problem));
            return null;
        }

        if (!hasHash)
            return null;
        if (hash.ValueKind != JsonValueKind.String || !PasswordHashing.IsHash(hash.GetString()!))
        {
            errors.Add(errors.Texts.NotAPasswordHash(Where(PasswordHashKey), Where(PasswordKey)));
            return null;
        }

        return hash.GetString();
    }

    /// <summary>
    /// Reads what the editor shows for a single file: <c>{ "perms": {...} }</c>, the entry of the file without its name (which is the file's own).
    /// </summary>
    /// <param name="texts">How the errors are worded (see <see cref="ParseFolder"/>).</param>
    /// <param name="allowsPlainPasswords">See <see cref="ParseFolder"/>.</param>
    public static ParseResult<PermsBlock> ParseFileBody(string text, ConfigTexts? texts = null, bool allowsPlainPasswords = false)
    {
        Problems errors = new(texts ?? ConfigTexts.Default);
        using JsonDocument? document = Open(text, errors);
        if (document is null)
            return ParseResult<PermsBlock>.Failed(errors);

        Dictionary<string, JsonElement>? root = ReadObject(document.RootElement, "", [PermsKey, PasswordKey, PasswordHashKey], required: [PermsKey],
            errors);
        PermsBlock? perms = root is null ? null : ParsePerms(root[PermsKey], PermsKey, errors);
        if (root is not null)
            _ = ParsePassword(root, "", allowsPlainPasswords, errors);

        return errors.Count > 0 || perms is null
            ? ParseResult<PermsBlock>.Failed(errors)
            : ParseResult<PermsBlock>.Ok(perms);
    }

    private static JsonDocument? Open(string text, Problems errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(errors.Texts.Empty);
            return null;
        }

        if (text.Length > MaxLength)
        {
            errors.Add(errors.Texts.TooLong);
            return null;
        }

        try
        {
            return JsonDocument.Parse(text, DocumentOptions);
        }
        catch (JsonException e)
        {
            // The message is meant for programmers: it says where on its own, and tells to change options that are not the user's to change
            string message = e.Message.Split(" LineNumber:")[0].Split(" Path:")[0].Replace(" Change the reader options.", "")
                .Replace(", when isFinalBlock is true", "");
            errors.Add(e.LineNumber is { } line
                ? errors.Texts.NotJson((int)line + 1, (e.BytePositionInLine ?? 0) + 1, message)
                : errors.Texts.NotJsonAnywhere(message));
            return null;
        }
    }

    /// <summary>
    /// The properties of the object by name. Reports (and returns null for) anything but an object, and a property that is not allowed, is
    /// there twice or is missing although required. Comments between the properties are not part of the value, they are skipped.
    /// </summary>
    private static Dictionary<string, JsonElement>? ReadObject(JsonElement element, string path, string[] allowed, string[] required,
        Problems errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(errors.Texts.NotAnObject(path));
            return null;
        }

        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        bool valid = true;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string propertyPath = path == "" ? property.Name : path + "." + property.Name;
            if (!allowed.Contains(property.Name))
            {
                errors.Add(errors.Texts.UnknownProperty(propertyPath, allowed));
                valid = false;
            }
            else if (!properties.TryAdd(property.Name, property.Value))
            {
                errors.Add(errors.Texts.Repeated(propertyPath));
                valid = false;
            }
        }

        foreach (string key in required.Where(key => !properties.ContainsKey(key)))
        {
            errors.Add(errors.Texts.Required(path == "" ? key : path + "." + key));
            valid = false;
        }

        return valid ? properties : null;
    }

    private static string? ParseName(JsonElement element, string path, HashSet<string> names, IReadOnlySet<string>? existingFiles,
        Problems errors)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } name)
        {
            errors.Add(errors.Texts.NotAString($"{path}.{NameKey}"));
            return null;
        }

        if (!PathService.IsValid(name))
        {
            errors.Add(errors.Texts.NotAFileName($"{path}.{NameKey}", name));
            return null;
        }

        if (!names.Add(name))
        {
            errors.Add(errors.Texts.FileTwice($"{path}.{NameKey}", name));
            return null;
        }

        if (existingFiles is not null && !existingFiles.Contains(name))
        {
            errors.Add(errors.Texts.NoSuchFile($"{path}.{NameKey}", name));
            return null;
        }

        return name;
    }

    /// <summary>Reads a <c>perms</c> block. Everything but <c>inherit</c> is optional, and a list that is missing is an empty one.</summary>
    private static PermsBlock? ParsePerms(JsonElement element, string path, Problems errors)
    {
        string[] keys = [InheritKey, .. RwKeys, .. CdKeys, .. HistoryKeys];
        Dictionary<string, JsonElement>? properties = ReadObject(element, path, keys, required: [InheritKey], errors);
        if (properties is null)
            return null;

        bool valid = true;
        bool inherit = false;
        if (properties[InheritKey].ValueKind is JsonValueKind.True or JsonValueKind.False)
            inherit = properties[InheritKey].GetBoolean();
        else
        {
            errors.Add(errors.Texts.NotABoolean($"{path}.{InheritKey}"));
            valid = false;
        }

        // A principal can be in one list of each kind, not in several: what is allowed and denied can't both be meant.
        // (It may be in an rw, a cd and a history list: they are about different things.)
        Dictionary<string, HashSet<string>> lists = [];
        foreach (string key in keys.Skip(1))
        {
            HashSet<string>? list = ParseList(properties.GetValueOrDefault(key), $"{path}.{key}", errors);
            valid &= list is not null;
            lists[key] = list ?? [];
        }

        valid &= CheckExclusive(lists, RwKeys, path, errors);
        valid &= CheckExclusive(lists, CdKeys, path, errors);
        valid &= CheckExclusive(lists, HistoryKeys, path, errors);

        // Someone who is not logged in must never be able to change the entries: they may be allowed to read, or denied, and that is all
        foreach (string key in NoAnonymousKeys.Where(key => lists[key].Contains(Principal.Everyone)))
        {
            errors.Add(errors.Texts.EveryoneMayNotChange($"{path}.{key}"));
            valid = false;
        }

        return valid
            ? new PermsBlock(inherit, lists["rwAllow"], lists["rwReadOnly"], lists["rwDeny"], lists["cdAllow"], lists["cdCreateOnly"],
                lists["cdDeny"], lists["historyAllow"], lists["historyExistingOnly"], lists["historyDeny"])
            : null;
    }

    private static HashSet<string>? ParseList(JsonElement element, string path, Problems errors)
    {
        HashSet<string> list = new(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Undefined)
            return list;

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add(errors.Texts.NotPrincipals(path));
            return null;
        }

        bool valid = true;
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string itemPath = $"{path}[{index++}]";
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add(errors.Texts.NotAString(itemPath));
                valid = false;
            }
            else if (Principal.Normalize(item.GetString()!, out string? error, errors.Texts) is not { } principal)
            {
                errors.Add($"{itemPath}: {error}");
                valid = false;
            }
            else if (!list.Add(principal))
            {
                errors.Add(errors.Texts.PrincipalTwice(itemPath, item.GetString()!));
                valid = false;
            }
        }

        return valid ? list : null;
    }

    private static bool CheckExclusive(Dictionary<string, HashSet<string>> lists, string[] keys, string path, Problems errors)
    {
        bool valid = true;
        for (int i = 0; i < keys.Length; i++)
        {
            for (int j = i + 1; j < keys.Length; j++)
            {
                foreach (string principal in lists[keys[i]].Intersect(lists[keys[j]], StringComparer.Ordinal))
                {
                    errors.Add(errors.Texts.InExclusiveLists(path, principal, keys[i], keys[j]));
                    valid = false;
                }
            }
        }

        return valid;
    }

    /// <summary>The errors found so far, and how they are worded.</summary>
    private sealed class Problems(ConfigTexts texts) : List<string>
    {
        public ConfigTexts Texts { get; } = texts;
    }
}
