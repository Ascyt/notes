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
    ConfigTexts? texts = null, RequestLookups? lookups = null)
{
    private sealed record Cached(DateTime LastWriteUtc, long Length, FolderConfigState State);

    private readonly ConcurrentDictionary<string, Cached> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which folders have a configuration file: for those it knows, no look at the disk is needed to find out.</summary>
    private readonly ConfigIndex _index = new(locations);

    /// <summary>How many times a configuration file was written by the app, so that a read knows whether a write came in between.</summary>
    private long _writes;

    /// <summary>The configuration file of the directory (for the unzipped copy of an archived folder, the one next to its archive).</summary>
    public string ConfigPathOf(string physicalDirectory)
        => Path.Combine(locations.ConfigDirectoryOf(physicalDirectory), PathService.ConfigFileName);

    /// <summary>
    /// What the folder's configuration says: nothing if there is no file, otherwise what is in it, or why that can't be used. Inside a
    /// <see cref="ConfigReadScope"/>, and inside a request (<see cref="RequestLookups"/>), a folder is looked at on disk only the first time.
    /// </summary>
    public FolderConfigState Get(string physicalDirectory)
    {
        physicalDirectory = Path.TrimEndingDirectorySeparator(physicalDirectory);
        if (ConfigReadScope.TryGet(physicalDirectory, out FolderConfigState known))
            return known;

        FolderConfigState state = lookups is null ? Read(physicalDirectory) : lookups.Get("config", physicalDirectory, Read);
        ConfigReadScope.Remember(physicalDirectory, state);
        return state;
    }

    /// <summary>How many times a configuration was looked at on disk (for the tests that check a request reads each once).</summary>
    public long DiskReads => Interlocked.Read(ref _diskReads);

    private long _diskReads;

    private FolderConfigState Read(string physicalDirectory)
    {
        string path = ConfigPathOf(physicalDirectory);
        string configDirectory = Path.GetDirectoryName(path)!;
        if (_index.Knows(configDirectory))
        {
            // Most folders have no configuration of their own, and those that have one are read once: until the app changes it (or the
            // rescan finds it changed on the server), what was read is what it says
            if (!_index.Has(configDirectory))
                return FolderConfigState.Missing;
            if (_cache.TryGetValue(physicalDirectory, out Cached? known))
                return known.State;
        }

        _ = Interlocked.Increment(ref _diskReads);
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
    /// Brings every configuration file of every location up to date on disk, which reading does only in memory, and finds them for the index
    /// (from then on, finding out whether a folder has one needs no look at the disk). Done when the app starts, before anything else uses the
    /// entries, and by the soft restart. A file that can't be brought up to date is left as it is, and logged (<see cref="Get"/> treats it as
    /// invalid). A location that can't be walked completely is logged, and looked at on disk as before.
    /// </summary>
    /// <returns>How many files were rewritten.</returns>
    public int MigrateAll()
        => locations.All.Sum(location => MigrateAllIn(location));

    /// <summary><see cref="MigrateAll"/> for one location (the root, a bridge, an unzipped archived folder).</summary>
    private int MigrateAllIn(EntryLocation location)
    {
        // (Walks every directory of the location, which takes a while with many)
        logger.LogInformation("Looking for configuration files in {location}", location.PhysicalPath);
        if (WalkLocation(location) is not { } found)
            return 0;
        logger.LogInformation("Found {count} configuration file(s) in {location}", found.Count, location.PhysicalPath);

        int migratedCount = 0;
        foreach (FileInfo file in found)
        {
            string directory = file.DirectoryName!;
            try
            {
                string text = ReadFile(file.FullName);
                MigrationResult migrated = migrator.Migrate(text);
                if (migrated.Error is not null)
                    logger.LogError("The configuration {path} was not migrated: {error}", file.FullName, migrated.Error);
                else if (migrated.Changed)
                {
                    WriteText(directory, migrated.Text);
                    migratedCount++;
                    logger.LogInformation("Migrated the configuration {path} from {from} to {to}", file.FullName, migrated.From, migrator.Current);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogError(e, "Could not migrate {path}", file.FullName);
            }
        }

        return migratedCount;
    }

    /// <summary>
    /// Walks the location for its configuration files and puts them in the index, which from then on is the truth for it. Null (and the
    /// location is looked at on disk as before) if it can't be walked completely.
    /// </summary>
    private List<FileInfo>? WalkLocation(EntryLocation location)
    {
        long changesBefore = _index.Changes;
        List<FileInfo> found;
        try
        {
            found = ConfigIndex.Walk(location.PhysicalPath, isLocationRoot: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not look through {location} for its configurations; they are looked for on disk each time",
                location.PhysicalPath);
            _index.SetWalked(location.PhysicalPath, walked: false);
            return null;
        }

        // (What the app changed meanwhile was told to the index: what the walk didn't see yet is in it already, and is kept)
        string[] directories = [.. found.Select(file => file.DirectoryName!)];
        if (_index.Changes == changesBefore)
            _index.Replace(location.PhysicalPath, directories);
        else
            foreach (string directory in directories)
                _index.Add(directory);
        _index.SetWalked(location.PhysicalPath, walked: true);
        return found;
    }

    // ---- Keeping the index in step with what the operations change on disk. Each is told while the operation still holds the lock
    // ---- (StructureLock, or the archived folder's gate), right after the change.

    /// <summary>
    /// What is at and below the path changed (copied there, unzipped there, put back after a failure, or gone): it is looked through again.
    /// Costs a walk of it, as part of the operation that changed it.
    /// </summary>
    public void TreeChanged(string physicalPath)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        ForgetCachedBelow(path);
        if (!_index.Knows(path))
            return;
        try
        {
            _index.Replace(path, Directory.Exists(path)
                ? [.. ConfigIndex.Walk(path, isLocationRoot: false).Select(file => file.DirectoryName!)]
                : []);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // What is in it can't be known for certain any more: its location is looked at on disk until the next rescan
            logger.LogWarning(e, "Could not look through {path} for its configurations", path);
            if (locations.LocationOf(path) is { } location)
                _index.SetWalked(location.PhysicalPath, walked: false);
        }
    }

    /// <summary>The directory was renamed or moved (to a path in the same location, or another one), and nothing else changed.</summary>
    public void TreeMoved(string fromPhysicalPath, string toPhysicalPath)
    {
        string from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fromPhysicalPath));
        string to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(toPhysicalPath));
        ForgetCachedBelow(from);
        ForgetCachedBelow(to);
        if (_index.Knows(from) && _index.Knows(to))
            _index.Move(from, to);
        else
        {
            _index.Forget(from);
            TreeChanged(to);
        }
    }

    /// <summary>An archived folder's unzipped copy was mounted (as a location of its own): it is walked for the index.</summary>
    public void LocationMounted(EntryLocation location)
    {
        ForgetCachedBelow(location.PhysicalPath);
        _ = WalkLocation(location);
    }

    /// <summary>An archived folder's unzipped copy was unmounted: nothing is known of it any more.</summary>
    public void LocationUnmounted(EntryLocation location)
    {
        ForgetCachedBelow(location.PhysicalPath);
        _index.Forget(location.PhysicalPath);
        _index.SetWalked(location.PhysicalPath, walked: false);
    }

    private void ForgetCachedBelow(string path)
    {
        foreach (string directory in _cache.Keys)
            if (string.Equals(directory, path, StringComparison.OrdinalIgnoreCase)
                || directory.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                _ = _cache.TryRemove(directory, out _);
        RequestLookups.Changed();
    }

    /// <summary>
    /// Brings the index in line with the disk (a background job): finds the configuration files added, changed or deleted on the server by hand.
    /// Waits for nothing. What the app changed while it walked was told to the index already, and is kept.
    /// </summary>
    public void Rescan()
    {
        foreach (EntryLocation location in locations.All)
        {
            HashSet<string> before = new(_index.In(location), StringComparer.OrdinalIgnoreCase);
            if (WalkLocation(location) is not { } found)
                continue;

            // Changed on disk since it was read: read again when it is asked for next
            Dictionary<string, FileInfo> files = found.ToDictionary(file => file.FullName, StringComparer.OrdinalIgnoreCase);
            foreach ((string directory, Cached cached) in _cache)
                if (files.TryGetValue(ConfigPathOf(directory), out FileInfo? file)
                    && (cached.LastWriteUtc != file.LastWriteTimeUtc || cached.Length != file.Length))
                    _ = _cache.TryRemove(directory, out _);
            // Deleted: what was read of it is forgotten too
            foreach (string gone in before.Except(found.Select(file => file.DirectoryName!), StringComparer.OrdinalIgnoreCase))
                ForgetCachedBelow(gone);
        }
        RequestLookups.Changed();
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
        // (The temporary file goes where the file is: for an archived folder that is outside its unzipped copy, whose data is zipped)
        string path = ConfigPathOf(physicalDirectory);
        string temp = Path.Combine(locations.TempDirectoryFor(Path.GetDirectoryName(path)!, "config"), Guid.NewGuid().ToString("N"));
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
        _index.Add(Path.GetDirectoryName(path)!);
        RequestLookups.Changed();
        // Also what was read of it under another path: an archived folder's unzipped copy has the configuration next to its archive
        foreach (string directory in _cache.Keys)
            if (string.Equals(ConfigPathOf(directory), path, StringComparison.OrdinalIgnoreCase))
                _ = _cache.TryRemove(directory, out _);
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
        // (How its unzipping goes stays behind: the unzipping is of the file at its old name, and ends there)
        Update(physicalDirectory, "prepare the renaming of", text => ConfigText.GetFileBody(text, oldName) is { } body
            ? ConfigText.SetFileBody(text, newName, ConfigText.RemoveProperties(body, ConfigParser.UnzipKeys) ?? body)
            : ConfigText.RemoveFile(text, newName));
    }

    // ---- Unzipping an uploaded archive (Components.Upload.UploadedArchiveUnzipper): the file's entry says how it goes, so that it goes on
    // ---- after a restart and the listing can show it

    /// <summary>The file is an uploaded archive that is to be unzipped. Whether its entry says so now.</summary>
    public bool UnzipPending(string physicalDirectory, string name)
    {
        if (!EnsureExists(physicalDirectory))
            return false;
        Update(physicalDirectory, "mark the unzipping of", text => ConfigText.RemoveFileProperties(text, name, [ConfigParser.UnzipErrorKey]) is { } cleared
            ? ConfigText.SetFileProperty(cleared, name, ConfigParser.UnzipPendingKey, "true")
            : null);
        return Get(physicalDirectory).Config?.FindFile(name)?.UnzipPending == true;
    }

    /// <summary>Unzipping the file failed, for that reason: it stays a file, and its entry says why.</summary>
    public void UnzipFailed(string physicalDirectory, string name, string reason)
        => Update(physicalDirectory, "mark the failed unzipping of", text =>
            ConfigText.RemoveFileProperties(text, name, [ConfigParser.UnzipPendingKey]) is { } cleared
                ? ConfigText.SetFileProperty(cleared, name, ConfigParser.UnzipErrorKey, JsonQuote(reason))
                : null);

    /// <summary>Unzipping the file was cancelled: it is a file like any other (its entry says nothing about unzipping any more).</summary>
    public void UnzipCancelled(string physicalDirectory, string name)
        => Update(physicalDirectory, "unmark the unzipping of", text => ConfigText.RemoveFileProperties(text, name, ConfigParser.UnzipKeys));

    private static string JsonQuote(string value)
        => System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public void FileRenamed(string physicalDirectory, string oldName, string newName)
        => Update(physicalDirectory, "rename", text => ConfigText.RenameFile(text, oldName, newName) is { } renamed
            ? ConfigText.RemoveFileProperties(renamed, newName, ConfigParser.UnzipKeys)
            : null);

    /// <summary>A file was deleted from the folder (or moved out of it).</summary>
    public void FileRemoved(string physicalDirectory, string name)
        => Update(physicalDirectory, "remove", text => ConfigText.RemoveFile(text, name));

    // ---- Passwords travel with what they protect, unlike the permissions: a copy must not be a way around one. What was copied without its
    // ---- configuration (by someone who doesn't manage it) gets one that says nothing but the password.

    /// <summary>The directory (a copy, or moved) is protected by the password with that hash, as what it came from was.</summary>
    /// <returns>Whether it is now.</returns>
    public bool DirectoryPasswordCarried(string physicalDirectory, string hash)
    {
        if (Get(physicalDirectory).Config?.PasswordHash == hash)
            return true;
        if (!EnsureExists(physicalDirectory))
            return false;
        Update(physicalDirectory, "carry the password into", text => ConfigText.SetPasswordHash(text, hash));
        return Get(physicalDirectory).Config?.PasswordHash == hash;
    }

    /// <summary>The file (a copy, or moved) is protected by the password with that hash, as what it came from was.</summary>
    /// <returns>Whether it is now.</returns>
    public bool FilePasswordCarried(string physicalDirectory, string name, string hash)
    {
        if (Get(physicalDirectory).Config?.FindFile(name)?.PasswordHash == hash)
            return true;
        if (!EnsureExists(physicalDirectory))
            return false;
        // (Its permissions don't travel: the entry it got where it went changes nothing, and so does this one, but for the password)
        string body = "{ \"perms\": { \"inherit\": true }, \"" + ConfigParser.PasswordHashKey + "\": "
            + System.Text.Json.JsonSerializer.Serialize(hash) + " }";
        Update(physicalDirectory, "carry the password of a file into", text => ConfigText.SetFileBody(text, name, body));
        return Get(physicalDirectory).Config?.FindFile(name)?.PasswordHash == hash;
    }

    /// <summary>
    /// Gives the folder a configuration if it has none: one that changes nothing (the permissions inherit, and what the history keeps is what
    /// the folders above say). Whether it has one now.
    /// </summary>
    private bool EnsureExists(string physicalDirectory)
    {
        if (File.Exists(ConfigPathOf(physicalDirectory)))
            return true;
        try
        {
            WriteText(physicalDirectory, ConfigText.Default(FileNamesOf(physicalDirectory), migrator.Current, IsLeftOutOfHistory(physicalDirectory)));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not give {directory} a configuration", physicalDirectory);
            return false;
        }
    }

    /// <summary>What the folders above say about <c>gitignore</c> for the folder (the nearest one that says anything), false if none does.</summary>
    public bool IsLeftOutOfHistory(string physicalDirectory)
    {
        for (string? folder = locations.ParentOf(physicalDirectory); folder is not null; folder = locations.ParentOf(folder))
            if (Get(folder).Config is { } config)
                return config.Gitignore;
        return false;
    }

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
