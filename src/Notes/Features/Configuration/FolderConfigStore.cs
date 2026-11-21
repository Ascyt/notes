using System.Collections.Concurrent;
using System.Text;
using Notes.Features.Bridges;
using Notes.Features.Configuration.Migrations;
using Notes.Features.Helpers;

namespace Notes.Features.Configuration;

/// <summary>
/// Reads and writes the configuration file of each folder (<see cref="PathService.ConfigFileName"/>), and keeps its file entries in step with
/// the files of the folder. The parsed files are cached for as long as they don't change on disk.
/// </summary>
/// <remarks>
/// Whoever changes the files of a folder tells the store (<see cref="FileAdded"/>, <see cref="FileRenamed"/>, <see cref="FileRemoved"/>) while
/// still holding the <see cref="StructureLock"/>, and so does everything that writes a configuration. The store takes no lock itself.
/// A file that is from an older version of the format (see <see cref="ConfigMigrator"/>) is always read as the current one: what the store
/// gives out is brought up to date, in memory. What is on disk is brought up to date by <see cref="MigrateAll"/> when the app starts, and by
/// any write (reads don't write: they don't hold the lock).
/// </remarks>
public sealed class FolderConfigStore(EntryLocations locations, ConfigMigrator migrator, ILogger<FolderConfigStore> logger,
    ConfigTexts? texts = null)
{
    private sealed record Cached(DateTime LastWriteUtc, long Length, FolderConfigState State);

    private readonly ConcurrentDictionary<string, Cached> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times a configuration file was written by the app, so that a read knows whether a write came in between.</summary>
    private long _writes;

    private static string ConfigPathOf(string physicalDirectory)
        => Path.Combine(physicalDirectory, PathService.ConfigFileName);

    /// <summary>
    /// What the folder's configuration says: nothing if there is no file, otherwise what is in it, or why that can't be used. Inside a
    /// <see cref="ConfigReadScope"/> a folder is looked at on disk only the first time.
    /// </summary>
    public FolderConfigState Get(string physicalDirectory)
    {
        physicalDirectory = Path.TrimEndingDirectorySeparator(physicalDirectory);
        if (ConfigReadScope.TryGet(physicalDirectory, out FolderConfigState known))
            return known;

        FolderConfigState state = Read(physicalDirectory);
        ConfigReadScope.Remember(physicalDirectory, state);
        return state;
    }

    private FolderConfigState Read(string physicalDirectory)
    {
        string path = ConfigPathOf(physicalDirectory);
        FileInfo file = new(path);
        if (!file.Exists)
        {
            _ = _cache.TryRemove(physicalDirectory, out _);
            return FolderConfigState.Missing;
        }

        if (_cache.TryGetValue(physicalDirectory, out Cached? cached) && cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
            return cached.State;

        // A write between looking at the file and reading it could leave what was read cached as what the new file says (which may have the
        // same time and length): only what no write overtook is kept
        long writesBefore = Interlocked.Read(ref _writes);
        FolderConfigState loaded = Load(path, physicalDirectory);
        if (Interlocked.Read(ref _writes) == writesBefore)
            _cache[physicalDirectory] = new Cached(file.LastWriteTimeUtc, file.Length, loaded);
        return loaded;
    }

    private FolderConfigState Load(string path, string physicalDirectory)
    {
        string text;
        try
        {
            text = ReadFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not read {path}", path);
            return new FolderConfigState(null, [(texts ?? ConfigTexts.Default).CantBeRead]);
        }

        MigrationResult migrated = migrator.Migrate(text);
        if (migrated.Error is not null)
        {
            logger.LogWarning("The configuration of {directory} can't be brought up to date, so only admins have access there: {error}",
                physicalDirectory, migrated.Error);
            return new FolderConfigState(null, [migrated.Error]);
        }

        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(migrated.Text, existingFiles: null, texts);
        if (!parsed.IsValid)
            logger.LogWarning("The configuration of {directory} is invalid, so only admins have access there:\n{errors}", physicalDirectory,
                parsed.ErrorText);

        return new FolderConfigState(parsed.Value, parsed.Errors);
    }

    /// <summary>
    /// The text of the folder's configuration file, brought up to date if it is from an older version (as it is if that can't be done, which
    /// <see cref="Get"/> then says), or null if there is none.
    /// </summary>
    public string? ReadText(string physicalDirectory)
    {
        string path = ConfigPathOf(physicalDirectory);
        return File.Exists(path) ? migrator.Migrate(ReadFile(path)).Text : null;
    }

    /// <summary>
    /// Brings every configuration file under the root directory up to date on disk, which reading does only in memory. Done once when the app
    /// starts, before anything else uses the entries. A file that can't be brought up to date is left as it is, and logged (<see cref="Get"/>
    /// treats it as invalid).
    /// </summary>
    /// <returns>How many files were rewritten.</returns>
    public int MigrateAll()
        => locations.All.Sum(location => MigrateAllIn(location.PhysicalPath));

    /// <summary><see cref="MigrateAll"/> for the physical directory of one location (the root, or a bridge).</summary>
    private int MigrateAllIn(string root)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            // Links are never followed, they could lead out of the root (see PathService)
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };

        int migratedCount = 0;
        // (Listed before anything is written, so that writing doesn't change what is listed)
        foreach (string path in Directory.EnumerateFiles(root, PathService.ConfigFileName, options).ToList())
        {
            string directory = Path.GetDirectoryName(path)!;
            // The folder of the app's own data is no part of the entries, nor is what a bridge hides
            if (Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar)[0] == PathService.ReservedName
                || locations.IsHiddenByBridge(directory))
                continue;

            try
            {
                string text = ReadFile(path);
                MigrationResult migrated = migrator.Migrate(text);
                if (migrated.Error is not null)
                    logger.LogError("The configuration {path} was not migrated: {error}", path, migrated.Error);
                else if (migrated.Changed)
                {
                    WriteText(directory, migrated.Text);
                    migratedCount++;
                    logger.LogInformation("Migrated the configuration {path} from {from} to {to}", path, migrated.From, migrator.Current);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogError(e, "Could not migrate {path}", path);
            }
        }

        return migratedCount;
    }

    /// <summary>
    /// Replaces the folder's configuration file, in one step (what is read meanwhile is the old file or the new one, never half of it). The
    /// text is not checked here. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> if it can't be written.
    /// </summary>
    public void WriteText(string physicalDirectory, string text)
    {
        // Written into the app's data of its location (the root or a bridge), where no listing shows it, and put in place of the file (same
        // volume, so it is a rename). File.Replace rather than a move over it: only it replaces a file on Windows that is being read (by a
        // reader that allows that, as ReadFile does), but it needs a file to replace, so a folder's first configuration is moved in.
        string path = ConfigPathOf(physicalDirectory);
        string temp = Path.Combine(locations.TempDirectoryFor(physicalDirectory, "config"), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null);
            else
                File.Move(temp, path);
        }
        finally
        {
            // Left behind only if it wasn't put in place
            File.Delete(temp);
        }

        // (After the file is in place: a read that began before it may have read the old one)
        _ = Interlocked.Increment(ref _writes);
        _ = _cache.TryRemove(Path.TrimEndingDirectorySeparator(physicalDirectory), out _);
    }

    /// <summary>
    /// The text of a configuration file. While it is read, <see cref="WriteText"/> may still replace it: the reading goes on with the old file.
    /// </summary>
    private static string ReadFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
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

    /// <summary>
    /// A file is about to be renamed: its new name gets an entry like the old one before the file is moved, and <see cref="FileRenamed"/>
    /// removes the old one after. Reading doesn't wait for the <see cref="StructureLock"/>, so otherwise the file would, for a moment, be
    /// under its new name without its own entry, with the permissions of the folder, which may give more. A name that only changes in case
    /// needs nothing: one entry is for both spellings.
    /// </summary>
    public void FileRenaming(string physicalDirectory, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return;

        // Without an entry of its own the file has the folder's permissions, under either name: an entry the new name has (left over) goes
        Update(physicalDirectory, "prepare the renaming of", text => ConfigText.GetFileBody(text, oldName) is { } body
            ? ConfigText.SetFileBody(text, newName, body)
            : ConfigText.RemoveFile(text, newName));
    }

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

            // Edited as the current version has it (a file from an older one is written as the current one then)
            string text = ReadFile(path);
            MigrationResult migrated = migrator.Migrate(text);
            if (migrated.Error is not null)
            {
                logger.LogWarning("Could not {what} a file entry in {path}: {error}", what, path, migrated.Error);
                return;
            }

            if (edit(migrated.Text) is not { } updated)
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
