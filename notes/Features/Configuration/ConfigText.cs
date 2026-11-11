using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Semver;

namespace notes.Features.Configuration;

/// <summary>
/// Edits the text of a folder's configuration file (JSON with comments) without reading it into an object and writing it out again, which
/// would lose the comments and the way it is laid out. Only the parts that change are touched: the file entries (<c>files</c>) are kept in step
/// with the files of the folder as they are added, renamed and removed (see <see cref="FolderConfigStore"/>), and the editor of a single file
/// shows and replaces one entry. Every method returns null if the text can't be edited: it is not valid JSON, or has no <c>files</c> array.
/// </summary>
public static class ConfigText
{
    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = false, MaxDepth = 16 };

    private static readonly JsonSerializerOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Where an entry of <c>files</c> is in the text. All positions are byte offsets, the end ones exclusive; -1 where there is none.</summary>
    private sealed record EntrySpan(string? Name, int Start, int End, int NameProperty, int NamePropertyEnd, int NameValue, int NameValueEnd, int OtherProperty, int OtherPropertyEnd);

    private sealed record FilesSpan(int ArrayStart, int ArrayEnd, List<EntrySpan> Entries);

    // ---- What the configuration of a folder starts as

    /// <summary>
    /// The text of a configuration that was never written: it permits nothing beyond what the folder above does, keeps the folder in the
    /// history, and has an entry for every file.
    /// </summary>
    public static string Default(IEnumerable<string> fileNames, SemVersion version)
    {
        StringBuilder text = new();
        text.Append($$"""
                    {
                        // The version of this file's format. The app keeps it up to date: don't change it.
                        "version": "{{version}}",
                        // Who may do what in this folder. Subfolders and files start from these permissions.
                        // "inherit": true also applies what the folder above gives to those the lists don't mention; false gives them nothing.
                        // Principals: "u:<user name>", "g:<group>", "r:<role>", "l" for all who are logged in, or "a" for all users (who may only be allowed to read and to see the history).
                        // Admins may always do everything.
                        // Lists: rwAllow, rwReadOnly, rwDeny (see and change what is in it), cdAllow, cdCreateOnly, cdDeny (create and delete),
                        // historyAllow, historyExistingOnly (only of what exists now), historyDeny (see earlier states).
                        "perms": {
                            "inherit": true
                        },
                        // true leaves the files of this folder and its subfolders out of the history (configuration files are always kept).
                        // A subfolder with a configuration of its own decides for itself.
                        "gitignore": false,
                        // The files of this folder, which the app keeps up to date. A file with "inherit": true and no lists has the permissions of the folder.
                        "files": [
                    """);

        string[] names = [.. fileNames.Order(StringComparer.OrdinalIgnoreCase)];
        for (int i = 0; i < names.Length; i++)
            text.Append("\n        ").Append(NewEntry(names[i])).Append(i < names.Length - 1 ? "," : "");

        text.Append(names.Length > 0 ? "\n    ]\n}\n" : "]\n}\n");
        return text.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>The text of a single file's configuration (the editor of a file shows this) that was never written.</summary>
    public static string DefaultFileBody()
        => "{\n    \"perms\": {\n        \"inherit\": true\n    }\n}\n";

    private static string NewEntry(string name)
        => $"{{ \"name\": {Quote(name)}, \"perms\": {{ \"inherit\": true }} }}";

    private static string Quote(string text)
        => JsonSerializer.Serialize(text, StringOptions);

    // ---- The version

    /// <summary>
    /// The text with <c>version</c> set to the version: its value replaced if the root object has the property, otherwise the property added
    /// first. Nothing else changes, comments included. Null if the text isn't valid JSON or its root isn't an object.
    /// </summary>
    public static string? SetVersion(string text, SemVersion version)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string value = Quote(version.ToString());
        try
        {
            Utf8JsonReader reader = new(bytes, ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            int open = (int)reader.TokenStartIndex;
            int firstProperty = -1;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (firstProperty < 0)
                    firstProperty = (int)reader.TokenStartIndex;

                bool isVersion = reader.ValueTextEquals(ConfigParser.VersionKey);
                _ = reader.Read();
                if (isVersion && reader.TokenType == JsonTokenType.String)
                    return Encoding.UTF8.GetString(Replace(bytes, (int)reader.TokenStartIndex, (int)reader.BytesConsumed, value));

                reader.Skip();
            }

            if (reader.TokenType != JsonTokenType.EndObject)
                return null;

            // Added first, laid out like the properties are: on a line of its own if they are, otherwise next to them
            string property = $"\"{ConfigParser.VersionKey}\": {value}";
            string inserted = firstProperty < 0 ? "\n" + IndentOfLine(bytes, open) + "    " + property + "\n" + IndentOfLine(bytes, open)
                : StartsLine(bytes, firstProperty) ? "\n" + IndentOfLine(bytes, firstProperty) + property + ","
                : " " + property + ",";
            return Encoding.UTF8.GetString(Replace(bytes, open + 1, open + 1, inserted));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- The names of the files

    /// <summary>The names of the entries of <c>files</c>.</summary>
    public static IReadOnlyList<string>? FileNames(string text)
        => Find(Encoding.UTF8.GetBytes(text))?.Entries.Select(entry => entry.Name ?? "").ToList();

    /// <summary>The text with an entry for the file, unless there is one already.</summary>
    public static string? AddFile(string text, string name)
        => Edit(text, (bytes, files) => FindEntry(files, name) is null ? Insert(bytes, files, NewEntry(name)) : bytes);

    /// <summary>The text without the entry of the file, and the comma that belongs to it.</summary>
    public static string? RemoveFile(string text, string name)
        => Edit(text, (bytes, files) =>
        {
            if (FindEntry(files, name) is not { } entry)
                return bytes;

            // The comma after it goes with it, and the white space up to what follows (which may be a comment of the next entry, that stays).
            // The last one has none after it: it takes the comma before it along instead.
            int index = files.Entries.IndexOf(entry);
            int afterComma = AfterSeparator(bytes, entry.End);
            (int from, int to) = afterComma >= 0 ? (entry.Start, afterComma)
                : index > 0 ? (files.Entries[index - 1].End, entry.End)
                : (files.ArrayStart + 1, entry.End);
            return Replace(bytes, from, to, "");
        });

    /// <summary>
    /// The text with the entry of the file under another name, which is all that changes (what its comments say stays). An entry the new name
    /// has already (left over from a file that was removed from outside the app) goes: the file takes its own permissions along, and there
    /// must never be two entries of one name.
    /// </summary>
    public static string? RenameFile(string text, string oldName, string newName)
    {
        // (Changing only the case finds the file's own entry under the new name)
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) && FileNames(text)?.Contains(newName, StringComparer.OrdinalIgnoreCase) == true)
            text = RemoveFile(text, newName)!;

        return Edit(text, (bytes, files) => FindEntry(files, oldName) is { NameValue: >= 0 } entry
            ? Replace(bytes, entry.NameValue, entry.NameValueEnd, Quote(newName))
            : bytes);
    }

    // ---- The entry of a single file

    /// <summary>
    /// The entry of the file as the editor of a single file shows it: the object without its name, which is the file's own. Null if the file has
    /// no entry (then there is nothing but the default to show), or the text can't be read.
    /// </summary>
    public static string? GetFileBody(string text, string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (Find(bytes) is not { } files || FindEntry(files, name) is not { } entry)
            return null;

        // Name and comma go (what is written between the properties stays); if the name is the only property there is no comma
        int afterComma = entry.NameProperty < 0 ? -1 : AfterSeparator(bytes, entry.NamePropertyEnd);
        (int from, int to) = entry.NameProperty < 0 ? (0, 0)
            : entry.OtherProperty < 0 ? (entry.NameProperty, entry.NamePropertyEnd)
            : entry.NameProperty < entry.OtherProperty && afterComma >= 0 ? (entry.NameProperty, afterComma)
            : entry.NameProperty < entry.OtherProperty ? (entry.NameProperty, entry.OtherProperty)
            : (entry.OtherPropertyEnd, entry.NamePropertyEnd);

        byte[] body = Replace(bytes[entry.Start..entry.End], from - entry.Start, to - entry.Start, "");
        // It sat inside the array, indented, and is shown on its own
        return Dedent(Encoding.UTF8.GetString(body), IndentOfLine(bytes, entry.Start)) + "\n";
    }

    /// <summary>
    /// The text with the entry of the file replaced by <paramref name="body"/> (what <see cref="GetFileBody"/> returns, as edited), or added
    /// if there is none. The comments the body has stay. Null if the text can't be edited, or the body isn't an object.
    /// </summary>
    public static string? SetFileBody(string text, string name, string body)
    {
        body = body.ReplaceLineEndings("\n");
        return Edit(text, (bytes, files) =>
        {
            int brace = OpeningBrace(body);
            if (brace < 0)
                return null;

            string afterBrace = body[(brace + 1)..];
            int firstNonSpace = afterBrace.Length - afterBrace.TrimStart().Length;
            string indent = afterBrace[..firstNonSpace].Contains('\n')
                ? afterBrace[(afterBrace.LastIndexOf('\n', firstNonSpace - 1) + 1)..firstNonSpace]
                : "    ";

            // Comments above the opening brace would end up between the entries, where the editor of the file doesn't show them (it shows the object).
            // They go inside it instead, on lines of their own (what follows a "//" on its line would be part of the comment).
            string leading = body[..brace].Trim();
            if (leading.Length > 0)
            {
                string rest = afterBrace[..firstNonSpace].Contains('\n') ? afterBrace : "\n" + indent + afterBrace.TrimStart();
                afterBrace = "\n" + indent + leading.Replace("\n", "\n" + indent) + rest;
                firstNonSpace = 1;
            }

            // The name goes first inside the object, laid out the way the object is
            string nameProperty = $"\"name\": {Quote(name)},";
            string inserted = afterBrace[..firstNonSpace].Contains('\n') ? "\n" + indent + nameProperty : " " + nameProperty;
            string entry = ("{" + inserted + afterBrace).TrimEnd();

            // A new entry is indented by Insert; one that is replaced stays where it is
            return FindEntry(files, name) is { } existing
                ? Replace(bytes, existing.Start, existing.End, entry.Replace("\n", "\n" + IndentOfLine(bytes, existing.Start)))
                : Insert(bytes, files, entry);
        });
    }

    // ---- Where things are

    private static string? Edit(string text, Func<byte[], FilesSpan, byte[]?> edit)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (Find(bytes) is not { } files)
            return null;

        byte[]? edited = edit(bytes, files);
        return edited is null ? null : Encoding.UTF8.GetString(edited);
    }

    private static EntrySpan? FindEntry(FilesSpan files, string name)
        => files.Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the <c>files</c> array of the root object and each entry of it. Null if the text isn't valid JSON or has no such array.</summary>
    private static FilesSpan? Find(byte[] json)
    {
        try
        {
            Utf8JsonReader reader = new(json, ReaderOptions);
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                    continue;

                bool isFiles = reader.ValueTextEquals(ConfigParser.FilesKey);
                _ = reader.Read();
                if (!isFiles)
                {
                    reader.Skip();
                    continue;
                }

                return reader.TokenType == JsonTokenType.StartArray ? ReadFilesArray(ref reader) : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static FilesSpan? ReadFilesArray(ref Utf8JsonReader reader)
    {
        int arrayStart = (int)reader.TokenStartIndex;
        List<EntrySpan> entries = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                return null;

            int start = (int)reader.TokenStartIndex;
            string? name = null;
            int nameProperty = -1, nameEnd = -1, nameValue = -1, nameValueEnd = -1, other = -1, otherEnd = -1;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                int propertyStart = (int)reader.TokenStartIndex;
                bool isName = reader.ValueTextEquals(ConfigParser.NameKey);
                _ = reader.Read();
                int valueStart = (int)reader.TokenStartIndex;
                if (isName && reader.TokenType == JsonTokenType.String)
                {
                    name = reader.GetString();
                    (nameProperty, nameEnd, nameValue, nameValueEnd) = (propertyStart, (int)reader.BytesConsumed, valueStart, (int)reader.BytesConsumed);
                }
                else
                {
                    reader.Skip();
                    (other, otherEnd) = (propertyStart, (int)reader.BytesConsumed);
                }
            }

            entries.Add(new EntrySpan(name, start, (int)reader.BytesConsumed, nameProperty, nameEnd, nameValue, nameValueEnd, other, otherEnd));
        }

        return reader.TokenType == JsonTokenType.EndArray ? new FilesSpan(arrayStart, (int)reader.TokenStartIndex, entries) : null;
    }

    // ---- Changing it

    private static byte[] Replace(byte[] bytes, int from, int to, string replacement)
    {
        byte[] inserted = Encoding.UTF8.GetBytes(replacement);
        byte[] result = new byte[bytes.Length - (to - from) + inserted.Length];
        bytes.AsSpan(0, from).CopyTo(result);
        inserted.CopyTo(result, from);
        bytes.AsSpan(to).CopyTo(result.AsSpan(from + inserted.Length));
        return result;
    }

    /// <summary>
    /// Adds the entry (which may span lines, indented from its own first line) after the last one, on a line of its own if the others are
    /// (otherwise next to them).
    /// </summary>
    private static byte[] Insert(byte[] bytes, FilesSpan files, string entry)
    {
        if (files.Entries.Count > 0)
        {
            EntrySpan last = files.Entries[^1];
            if (!StartsLine(bytes, last.Start))
                return Replace(bytes, last.End, last.End, ", " + entry);

            string indent = IndentOfLine(bytes, last.Start);
            return Replace(bytes, last.End, last.End, ",\n" + indent + entry.Replace("\n", "\n" + indent));
        }

        // Nothing in it yet: the entry goes on its own line, with the bracket that closes the array below it. Comments (all that can be in it)
        // stay behind the entry, which is the only one: no comma.
        string arrayIndent = IndentOfLine(bytes, files.ArrayStart);
        string inner = arrayIndent + "    ";
        string indented = inner + entry.Replace("\n", "\n" + inner);
        bool onlySpace = bytes[(files.ArrayStart + 1)..files.ArrayEnd].All(b => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n');
        return onlySpace
            ? Replace(bytes, files.ArrayStart + 1, files.ArrayEnd, "\n" + indented + "\n" + arrayIndent)
            : Replace(bytes, files.ArrayStart + 1, files.ArrayStart + 1, "\n" + indented);
    }

    /// <summary>
    /// If a comma follows the position (with nothing but white space in between): the position after it and the white space behind it.
    /// Otherwise -1: a comment in between, or no comma because nothing follows.
    /// </summary>
    private static int AfterSeparator(byte[] bytes, int position)
    {
        int i = position;
        while (i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            i++;
        if (i >= bytes.Length || bytes[i] != ',')
            return -1;

        i++;
        while (i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            i++;
        return i;
    }

    /// <summary>The white space the line with the position starts with.</summary>
    private static string IndentOfLine(byte[] bytes, int position)
    {
        int lineStart = position;
        while (lineStart > 0 && bytes[lineStart - 1] != '\n')
            lineStart--;

        int end = lineStart;
        while (end < bytes.Length && bytes[end] is (byte)' ' or (byte)'\t')
            end++;

        return Encoding.UTF8.GetString(bytes[lineStart..end]);
    }

    /// <summary>Whether nothing but white space is before the position on its line.</summary>
    private static bool StartsLine(byte[] bytes, int position)
    {
        int i = position;
        while (i > 0 && bytes[i - 1] is (byte)' ' or (byte)'\t')
            i--;
        return i == 0 || bytes[i - 1] == '\n';
    }

    /// <summary>Takes up to <paramref name="indent"/> of white space off the start of every line but the first.</summary>
    private static string Dedent(string text, string indent)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        for (int i = 1; i < lines.Length; i++)
            lines[i] = lines[i].StartsWith(indent, StringComparison.Ordinal) ? lines[i][indent.Length..] : lines[i].TrimStart();
        return string.Join('\n', lines);
    }

    /// <summary>The index of the first character of the body that is not white space or a comment, if that is an opening brace; otherwise -1.</summary>
    private static int OpeningBrace(string body)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            Utf8JsonReader reader = new(bytes, ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return -1;

            // The reader works on bytes, the text on characters: only the part before the brace is needed to tell them apart
            return Encoding.UTF8.GetString(bytes, 0, (int)reader.TokenStartIndex).Length;
        }
        catch (JsonException)
        {
            return -1;
        }
    }
}
