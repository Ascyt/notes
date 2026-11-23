using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Configuration.Migrations;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.History;
using Notes.Features.Passwords;
using Notes.Features.Permissions;
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
    IPathService paths, ILogger<ConfigHandler> logger) : IEntryPostAction
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

        string defaultText = ConfigText.Default(FolderConfigStore.FileNamesOf(folder), migrator.Current, store.IsLeftOutOfHistory(folder));
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
                    return SaveFolder(request.HttpContext, physicalPath, text);
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

    private IActionResult SaveFolder(HttpContext context, string folder, string text)
    {
        IReadOnlySet<string> files = FolderConfigStore.FileNamesOf(folder);

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
        text = HashPasswords(text);

        // Every file has an entry: the ones the text lacks (files that came in from outside the app) get one that changes nothing
        foreach (string name in files.Order(StringComparer.OrdinalIgnoreCase).Where(name => parsed.Value!.FindFile(name) is null))
            text = ConfigText.AddFile(text, name) ?? text;

        // What was added must not have broken it: a configuration that doesn't hold up closes the folder to everyone but admins
        ParseResult<FolderConfig> completed = ConfigParser.ParseFolder(text, files, texts);
        if (!completed.IsValid)
        {
            logger.LogError("Adding the missing file entries made the configuration of {folder} invalid:\n{errors}", folder, completed.ErrorText);
            return Failed(StatusCodes.Status500InternalServerError,
                texts.FilesNotAdded + "\n" + completed.ErrorText);
        }

        Write(context.User, folder, text);
        UnlockNewPasswords(context, folder, before, completed.Value!);
        return Saved(text);
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
    private static Dictionary<string, Protection> ProtectionsIn(string folder, FolderConfig? config)
    {
        Dictionary<string, Protection> protections = new(StringComparer.OrdinalIgnoreCase);
        if (config is null)
            return protections;

        string folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (config.PasswordHash is { } hash)
            protections[folderPath] = new Protection(folderPath, hash);
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
        string path = Path.Combine(folder, PathService.ConfigFileName);
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
