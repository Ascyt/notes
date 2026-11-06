using System.Collections.Concurrent;
using System.Text;
using notes.Features.Helpers;

namespace notes.Features.Permissions;

/// <summary>
/// Reads and writes the configuration file of each folder (<see cref="PathService.ConfigFileName"/>), and keeps its file entries in step with
/// the files of the folder. The parsed files are cached for as long as they don't change on disk.
/// </summary>
/// <remarks>
/// Whoever changes the files of a folder tells the store (<see cref="FileAdded"/>, <see cref="FileRenamed"/>, <see cref="FileRemoved"/>) while
/// still holding the <see cref="StructureLock"/>, and so does everything that writes a configuration. The store takes no lock itself.
/// </remarks>
public sealed class FolderConfigStore(Config config, ILogger<FolderConfigStore> logger)
{
    private sealed record Cached(DateTime LastWriteUtc, long Length, FolderConfigState State);

    private readonly ConcurrentDictionary<string, Cached> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static string ConfigPathOf(string physicalDirectory)
        => Path.Combine(physicalDirectory, PathService.ConfigFileName);

    /// <summary>What the folder's configuration says: nothing if there is no file, otherwise what is in it, or why that can't be used.</summary>
    public FolderConfigState Get(string physicalDirectory)
    {
        physicalDirectory = Path.TrimEndingDirectorySeparator(physicalDirectory);
        string path = ConfigPathOf(physicalDirectory);
        FileInfo file = new(path);
        if (!file.Exists)
        {
            _ = _cache.TryRemove(physicalDirectory, out _);
            return FolderConfigState.Missing;
        }

        if (_cache.TryGetValue(physicalDirectory, out Cached? cached) && cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
            return cached.State;

        FolderConfigState state = Load(path, physicalDirectory);
        _cache[physicalDirectory] = new Cached(file.LastWriteTimeUtc, file.Length, state);
        return state;
    }

    private FolderConfigState Load(string path, string physicalDirectory)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not read {path}", path);
            return new FolderConfigState(null, ["The configuration file can't be read by the server."]);
        }

        ParseResult<FolderConfig> parsed = PermissionParser.ParseFolder(text, existingFiles: null);
        if (!parsed.IsValid)
            logger.LogWarning("The configuration of {directory} is invalid, so only admins have access there:\n{errors}", physicalDirectory, parsed.ErrorText);

        return new FolderConfigState(parsed.Value, parsed.Errors);
    }

    /// <summary>The text of the folder's configuration file as it is, or null if there is none.</summary>
    public string? ReadText(string physicalDirectory)
    {
        string path = ConfigPathOf(physicalDirectory);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// Replaces the folder's configuration file, in one step (what is read meanwhile is the old file or the new one, never half of it). The
    /// text is not checked here. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> if it can't be written.
    /// </summary>
    public void WriteText(string physicalDirectory, string text)
    {
        // Written next to the notes, where no listing shows it, and moved over the file (same volume, so it is a rename)
        string temp = Path.Combine(Path.GetFullPath(config.Dir), PathService.ReservedName, "temp", "config", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            File.Move(temp, ConfigPathOf(physicalDirectory), overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        _ = _cache.TryRemove(Path.TrimEndingDirectorySeparator(physicalDirectory), out _);
    }

    /// <summary>The names of the files in the folder, which is what the entries of its configuration are for.</summary>
    public static IReadOnlySet<string> FileNamesOf(string physicalDirectory)
        => Directory.EnumerateFiles(physicalDirectory)
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.Equals(name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // ---- Keeping the entries in step. A folder without a configuration file stays without one, and a failure here never fails what
    // ---- was done to the files: the entry is then missing (the file has the permissions of the folder) or has its old name, until an admin saves.

    /// <summary>A file was created in the folder (or came into it): it gets an entry that changes nothing, unless it has one already.</summary>
    public void FileAdded(string physicalDirectory, string name)
        => Update(physicalDirectory, "add", text => ConfigText.AddFile(text, name));

    public void FileRenamed(string physicalDirectory, string oldName, string newName)
        => Update(physicalDirectory, "rename", text => ConfigText.RenameFile(text, oldName, newName));

    /// <summary>A file was deleted from the folder (or moved out of it).</summary>
    public void FileRemoved(string physicalDirectory, string name)
        => Update(physicalDirectory, "remove", text => ConfigText.RemoveFile(text, name));

    private void Update(string physicalDirectory, string what, Func<string, string?> edit)
    {
        string path = ConfigPathOf(physicalDirectory);
        try
        {
            if (!File.Exists(path))
                return;

            string text = File.ReadAllText(path);
            if (edit(text) is not { } updated)
            {
                logger.LogWarning("Could not {what} a file entry in {path}: it can't be edited (is it valid?)", what, path);
                return;
            }

            if (updated != text)
                WriteText(physicalDirectory, updated);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not {what} a file entry in {path}", what, path);
        }
    }
}
