using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Configuration.Migrations;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.History;
using Notes.Features.Passwords;
using Notes.Features.Permissions;
using Notes.Features.ZippedFolders;
using Notes.Pages.ConfigEditor;

namespace Notes.Features.Configuration;

/// <summary>
/// Answers <c>?config</c>: the configuration of a folder (its configuration file, as text) or of a file (its entry in the configuration file of its
/// folder, as text without its name). A GET is made into a page, a POST with the new text saves it. The caller has made sure the user may manage
/// the configuration. The text is checked completely before anything is written (<see cref="ConfigParser"/>), and what is saved is the text as
/// it was sent, with its comments, plus the entries that the files of the folder need (and with the hash of a password in its place).
/// </summary>
/// <remarks>
/// Only the POST is an action: the GET is another view of the entry (principle 15), which the page of the entry shows (<see cref="CreateView"/>).
/// </remarks>
/// <remarks>
/// A <c>password</c> in what is sent is saved as its hash (<c>passwordHash</c>), and the editor then shows that. A password that is there may
/// only be changed or removed while the entry is unlocked, by admins too: otherwise editing the configuration would be a way around it.
/// </remarks>
public sealed class ConfigHandler(FolderConfigStore store, ConfigMigrator migrator, StructureLock structureLock, HistoryService history,
    ConfigSchema schema, ConfigTexts texts, EntryTexts entryTexts, PasswordLocks passwordLocks, PasswordUnlocks unlocks, Config config,
    IPathService paths, ZippedFolders.ArchivedFolders archives, Bridges.EntryLocations locations, ILogger<ConfigHandler> logger)
    : IEntryPostAction
{
    public const string ConfigParameter = "config";

    public string Parameter => ConfigParameter;
    public AccessNeeded Needs => AccessNeeded.ManagesConfiguration;

    public Task<IActionResult> PostAsync(EntryRequest request)
        => SaveAsync(request.Request, request.PhysicalPath);

    public ConfigEditorView CreateView(string virtualPath, string physicalPath)
    {
        bool isFile = File.Exists(physicalPath);
        string folder = isFile ? Path.GetDirectoryName(physicalPath)! : physicalPath;
        string? saved = store.ReadText(folder);

        // A configuration that was saved but doesn't hold up is said so, as the folder is closed to everyone else until it does
        // (the store says why: it also knows what couldn't be migrated)
        string? warning = null;
        if (saved is not null && store.Get(folder) is { IsInvalid: true } invalid)
            warning = texts.SavedIsInvalid + "\n"
                + ParseResult<FolderConfig>.Failed(invalid.Errors).ErrorText;

        if (isFile)
        {
            string? body = saved is null ? null : ConfigText.GetFileBody(saved, Path.GetFileName(physicalPath));
            return new ConfigEditorView
            {
                VirtualPath = virtualPath,
                PhysicalPath = physicalPath,
                IsFile = true,
                Contents = body ?? ConfigText.DefaultFileBody(),
                DefaultContents = ConfigText.DefaultFileBody(),
                Schema = schema.FileBody,
                Warning = warning
            };
        }

        // (A zipped folder's files are in its archive, whose own name is no file's)
        IEnumerable<string> fileNames = archives.IsZipped(folder) ? [] : FolderConfigStore.FileNamesOf(folder);
        string defaultText = ConfigText.Default(fileNames, migrator.Current, store.IsLeftOutOfHistory(folder));
        return new ConfigEditorView
        {
            VirtualPath = virtualPath,
            PhysicalPath = physicalPath,
            IsFile = false,
            Contents = saved ?? defaultText,
            DefaultContents = defaultText,
            Schema = schema.Folder,
            Warning = warning
        };
    }

    /// <summary>Saves the text in the body. Answers with the text as it is saved, which the editor then shows.</summary>
    private async Task<IActionResult> SaveAsync(HttpRequest request, string physicalPath)
    {
        string? sent;
        try
        {
            sent = await ReadBodyAsync(request.Body);
        }
        catch (DecoderFallbackException)
        {
            return Failed(StatusCodes.Status400BadRequest, texts.NotUtf8);
        }
        if (sent is null)
            return Failed(StatusCodes.Status413PayloadTooLarge, texts.TooLong);

        // The editor's lines end the way the browser's platform does. The file always has the same, so the entries that are added to it fit in.
        string text = sent.ReplaceLineEndings("\n");

        // Entries are added and removed as files come and go, so what is read here and written back must not be changed in between
        using (await structureLock.EnterAsync())
        {
            try
            {
                if (Directory.Exists(physicalPath))
                    return await SaveFolderAsync(request.HttpContext, physicalPath, text);
                if (File.Exists(physicalPath))
                    return SaveFile(request.HttpContext, physicalPath, text);

                return new NotFoundObjectResult(entryTexts.PathNotFound);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogError(e, "Could not save the configuration for {path}", physicalPath);
                return Failed(StatusCodes.Status500InternalServerError, texts.CantBeWritten);
            }
        }
    }

    private async Task<IActionResult> SaveFolderAsync(HttpContext context, string folder, string text)
    {
        // The files of a zipped folder are in its archive: their entries are neither checked nor added until it is unzipped
        string folderPath = Path.GetDirectoryName(store.ConfigPathOf(folder))!;
        IReadOnlySet<string>? files = archives.IsZipped(folderPath) ? null : FolderConfigStore.FileNamesOf(folder);

        // What was sent may be an older version's (pasted in, say): it is brought up to date, and then checked as the current version
        MigrationResult migrated = migrator.Migrate(text);
        if (migrated.Error is not null)
            return Failed(StatusCodes.Status400BadRequest, ParseResult<FolderConfig>.Failed([migrated.Error]).ErrorText);

        // Without a version it can't be told which one it is written for, and guessing would apply migrations to what may be up to date already
        if (migrated.From == ConfigVersions.Zero)
        {
            List<string> errors = [.. ConfigParser.ParseFolder(text, files, texts, allowsPlainPasswords: true).Errors];
            if (errors.Count == 0)
                errors.Add(texts.VersionRequired(ConfigParser.VersionKey, migrator.Current.ToString()));
            return Failed(StatusCodes.Status400BadRequest, ParseResult<FolderConfig>.Failed(errors).ErrorText);
        }

        text = migrated.Text;
        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(text, files, texts, allowsPlainPasswords: true);
        if (!parsed.IsValid)
            return Failed(StatusCodes.Status400BadRequest, parsed.ErrorText);

        FolderConfig? before = store.Get(folder).Config;
        if (RefusedPasswordChange(context, folder, before, parsed.Value!) is { } refused)
            return refused;
        if (RefusedArchiveChange(folderPath, before?.OwnArchive, parsed.Value!.OwnArchive) is { } refusedArchive)
            return refusedArchive;
        text = HashPasswords(text);

        // Every file has an entry: the ones the text lacks (files that came in from outside the app) get one that changes nothing
        foreach (string name in (files ?? new HashSet<string>()).Order(StringComparer.OrdinalIgnoreCase)
                     .Where(name => parsed.Value!.FindFile(name) is null))
            text = ConfigText.AddFile(text, name) ?? text;

        // What was added must not have broken it: a configuration that doesn't hold up closes the folder to everyone but admins
        ParseResult<FolderConfig> completed = ConfigParser.ParseFolder(text, files, texts);
        if (!completed.IsValid)
        {
            logger.LogError("Adding the missing file entries made the configuration of {folder} invalid:\n{errors}", folder, completed.ErrorText);
            return Failed(StatusCodes.Status500InternalServerError,
                texts.FilesNotAdded + "\n" + completed.ErrorText);
        }

        string? textBefore = store.ReadText(folder);
        Write(context.User, folder, text);
        UnlockNewPasswords(context, folder, before, completed.Value!);

        // What is on disk follows the configurations (ArchivedFolders.ReconcileAsync): archiving on makes what is in the folder its unzipped
        // copy, off puts it back, onLowestLevel archives the folders below that hold files. A new filetype or compressionLevel is used from the
        // next zipping on.
        if (!Equals(before?.OwnArchive, completed.Value!.OwnArchive))
        {
            // Below a folder that turns onLowestLevel on, each configuration leaves it to that one
            if (IsLowestLevel(completed.Value!.OwnArchive) && !IsLowestLevel(before?.OwnArchive))
                LeaveArchivingToAbove(context.User, folderPath);

            List<string> failed = await archives.ReconcileAsync(folderPath);
            if (failed.Any(path => string.Equals(path, Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath)), StringComparison.OrdinalIgnoreCase)))
            {
                // Nothing of the folder itself changed: neither does its configuration
                logger.LogWarning("Could not change whether {folder} is archived now", folderPath);
                if (textBefore is not null)
                    Write(context.User, folder, textBefore);
                _ = await archives.ReconcileAsync(folderPath);
                return Failed(StatusCodes.Status409Conflict, completed.Value!.Archive.Enabled ? texts.CantArchiveNow : texts.CantUnarchiveNow);
            }

            // Saved, but archiving inside an archived folder only adds work: what is in it is in that archive already
            if (completed.Value!.OwnArchive is { Enabled: true } && IsInsideArchivedFolder(folderPath))
                context.Response.Headers[ConfigWarningHeader] = Uri.EscapeDataString(texts.ArchiveInsideArchive);
        }

        return Saved(text);
    }

    /// <summary>What a saved configuration is answered with besides its text, if there is something about it worth knowing (URL-encoded).</summary>
    public const string ConfigWarningHeader = "X-Notes-Config-Warning";

    /// <summary>Whether a folder above the folder (physical) is archived (its unzipped copy, for one inside it).</summary>
    private bool IsInsideArchivedFolder(string folderPath)
    {
        for (string? up = locations.ParentOf(folderPath); up is not null; up = locations.ParentOf(up))
            if (archives.IsArchived(up))
                return true;
        return false;
    }

    private static bool IsLowestLevel(ArchiveSettings? archive)
        => archive is { Enabled: true, OnLowestLevel: true };

    /// <summary>
    /// The answer if the change of <c>archive</c> can't be made, or null. The root, a bridge and what holds a bridge can't be archived, as their
    /// place is the configuration's (an <c>onLowestLevel</c> one can: it isn't archived itself). <c>onLowestLevel</c> can't be turned on over
    /// entries with a password (archiving them that way would get around it, see docs/administration/archived-directories.md), nor over folders
    /// archived with settings of their own.
    /// </summary>
    private ObjectResult? RefusedArchiveChange(string folderPath, ArchiveSettings? before, ArchiveSettings? after)
    {
        bool archivesItself = after is { Enabled: true, OnLowestLevel: false };
        if (archivesItself && before is not { Enabled: true, OnLowestLevel: false }
            && (locations.All.Any(location => !location.IsArchive
                    && string.Equals(location.PhysicalPath, folderPath, StringComparison.OrdinalIgnoreCase))
                || locations.HoldsBridge(folderPath)))
            return Failed(StatusCodes.Status400BadRequest, texts.CantArchiveBridge);

        if (IsLowestLevel(after) && !IsLowestLevel(before))
        {
            (List<string> below, _) = archives.DirectoriesBelow(folderPath);
            List<string> withPasswords = [];
            List<string> archivedOnTheirOwn = [];
            foreach (string directory in below)
            {
                if (store.Get(directory).Config is not { } config)
                    continue;
                if (config.PasswordHash is not null)
                    withPasswords.Add(paths.ToVirtualPath(directory));
                withPasswords.AddRange(config.Files.Where(file => file.PasswordHash is not null)
                    .Select(file => paths.ToVirtualPath(Path.Combine(directory, file.Name))));
                if (config.OwnArchive is { Enabled: true })
                    archivedOnTheirOwn.Add(paths.ToVirtualPath(directory));
            }
            if (withPasswords.Count > 0)
                return Failed(StatusCodes.Status400BadRequest, texts.LowestLevelWithPasswords(withPasswords));
            if (archivedOnTheirOwn.Count > 0)
                return Failed(StatusCodes.Status400BadRequest, texts.LowestLevelOverArchived(archivedOnTheirOwn));
        }
        return null;
    }

    /// <summary>
    /// Sets <c>"archive": null</c> in the configuration of each folder below that has one (and says something else), so that the
    /// <c>onLowestLevel</c> folder decides for them; <c>{ "enabled": false }</c> set later cuts one off again. One that isn't valid is left.
    /// </summary>
    private void LeaveArchivingToAbove(ClaimsPrincipal user, string folderPath)
    {
        foreach (string directory in archives.DirectoriesBelow(folderPath).Directories)
        {
            if (store.Get(directory).Config is not { OwnArchive: not null } || store.ReadText(directory) is not { } current
                || ConfigText.SetArchiveToInherit(current) is not { } inheriting)
                continue;
            Write(user, directory, inheriting);
        }
    }

    private IActionResult SaveFile(HttpContext context, string physicalPath, string text)
    {
        ParseResult<PermsBlock> parsed = ConfigParser.ParseFileBody(text, texts, allowsPlainPasswords: true);
        if (!parsed.IsValid)
            return Failed(StatusCodes.Status400BadRequest, parsed.ErrorText);

        // The entry goes into the configuration of the folder (made now if it has none), whose other entries and comments stay
        string folder = Path.GetDirectoryName(physicalPath)!;
        string name = Path.GetFileName(physicalPath);
        string folderText = store.ReadText(folder) ?? ConfigText.Default(FolderConfigStore.FileNamesOf(folder), migrator.Current, store.IsLeftOutOfHistory(folder));
        string? withPassword = ConfigText.SetFileBody(folderText, name, text);
        if (withPassword is null)
            return Failed(StatusCodes.Status409Conflict,
                texts.FolderUnreadable);

        // The password of the file is checked as part of the folder, whose other entries are as they were
        FolderConfig? before = store.Get(folder).Config;
        if (ConfigParser.ParseFolder(withPassword, existingFiles: null, texts, allowsPlainPasswords: true).Value is { } sent
            && RefusedPasswordChange(context, folder, before, sent) is { } refused)
            return refused;
        string updated = HashPasswords(withPassword);

        // What it is now must hold up as a whole. (Not whether every entry has a file: that is the folder's to say when it is saved.)
        ParseResult<FolderConfig> whole = ConfigParser.ParseFolder(updated, existingFiles: null, texts);
        if (!whole.IsValid)
            return Failed(StatusCodes.Status409Conflict,
                texts.FolderInvalid + "\n"
                + whole.ErrorText);

        Write(context.User, folder, updated);
        UnlockNewPasswords(context, folder, before, whole.Value!);
        return Saved(ConfigText.GetFileBody(updated, name) ?? text);
    }

    // ---- Passwords

    /// <summary>
    /// The passwords of the folder and of its files, each with the entry it protects (see <see cref="PasswordLocks.ProtectionsOf"/>), by the
    /// physical path of that entry.
    /// </summary>
    private Dictionary<string, Protection> ProtectionsIn(string folder, FolderConfig? config)
    {
        Dictionary<string, Protection> protections = new(StringComparer.OrdinalIgnoreCase);
        if (config is null)
            return protections;

        string folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        // (That of an archived folder is the folder's, as PasswordLocks has it, also while it is unzipped)
        string protectedFolder = Path.GetDirectoryName(store.ConfigPathOf(folderPath))!;
        if (config.PasswordHash is { } hash)
            protections[protectedFolder] = new Protection(protectedFolder, hash);
        foreach (FileEntry file in config.Files)
            if (file.PasswordHash is { } fileHash)
                protections[Path.Combine(folderPath, file.Name)] = new Protection(Path.Combine(folderPath, file.Name), fileHash);
        return protections;
    }

    /// <summary>
    /// The answer if what is sent changes or removes a password of an entry that isn't unlocked (a password that is only being typed counts as a
    /// change, as its hash isn't made yet); null if it doesn't. Setting a password where there was none needs nothing.
    /// </summary>
    private ObjectResult? RefusedPasswordChange(HttpContext context, string folder, FolderConfig? before, FolderConfig sent)
    {
        Dictionary<string, Protection> after = ProtectionsIn(folder, sent);
        foreach ((string path, Protection old) in ProtectionsIn(folder, before))
        {
            if (after.TryGetValue(path, out Protection? kept) && kept.Hash == old.Hash)
                continue;
            if (!passwordLocks.IsUnlocked(context, old))
                return Failed(StatusCodes.Status403Forbidden, texts.PasswordChangeNeedsUnlock(paths.ToVirtualPath(path)));
        }

        return null;
    }

    /// <summary>The text with the hash of each password in place of the password (see <see cref="ConfigText.HashPasswords"/>).</summary>
    private string HashPasswords(string text)
        => ConfigText.HashPasswords(text, password => PasswordHashing.Hash(password, config.Passwords.WorkFactor))
           ?? throw new InvalidOperationException("A configuration that was parsed can't be read for its passwords.");

    /// <summary>Whoever just set a password knows it: what it protects stays unlocked for them, instead of asking for it right away.</summary>
    private void UnlockNewPasswords(HttpContext context, string folder, FolderConfig? before, FolderConfig saved)
    {
        Dictionary<string, Protection> old = ProtectionsIn(folder, before);
        foreach ((string path, Protection protection) in ProtectionsIn(folder, saved))
            if (!old.TryGetValue(path, out Protection? was) || was.Hash != protection.Hash)
                unlocks.Unlock(context, protection);
    }

    /// <summary>Writes the folder's configuration file, which the history sees as a save of it.</summary>
    private void Write(ClaimsPrincipal user, string folder, string text)
    {
        string path = store.ConfigPathOf(folder);
        history.BeforeChange(user, [path], text);
        store.WriteText(folder, text);
        history.AfterChange(user, [path], HistoryChangeKind.Save);
    }

    private static ContentResult Saved(string text)
        => new() { Content = text, ContentType = "text/plain; charset=utf-8" };

    private static ObjectResult Failed(int statusCode, string message)
        => new(message) { StatusCode = statusCode };

    /// <summary>
    /// The body as text, or null if it is longer than any configuration may be. Throws <see cref="DecoderFallbackException"/> if it isn't
    /// UTF-8: it is refused, not read with U+FFFD in place of what doesn't fit.
    /// </summary>
    private static async Task<string?> ReadBodyAsync(Stream body)
    {
        using StreamReader reader = new(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        StringBuilder text = new();
        char[] buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            _ = text.Append(buffer, 0, read);
            if (text.Length > ConfigParser.MaxLength)
                return null;
        }

        return text.ToString();
    }
}
