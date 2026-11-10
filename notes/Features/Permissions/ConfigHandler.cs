using System.Text;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Helpers;
using notes.Features.Permissions.Migrations;
using notes.Pages.ConfigEditor;

namespace notes.Features.Permissions;

/// <summary>
/// Answers <c>?config</c>: the permissions of a folder (its configuration file, as text) or of a file (its entry in the configuration file of its
/// folder, as text without its name). A GET is made into a page, a POST with the new text saves it. The caller has made sure the user may manage
/// permissions. The text is checked completely before anything is written (<see cref="PermissionParser"/>), and what is saved is the text as
/// it was sent, with its comments, plus the entries that the files of the folder need.
/// </summary>
public sealed class ConfigHandler(FolderConfigStore store, ConfigMigrator migrator, StructureLock structureLock, ILogger<ConfigHandler> logger)
{
    public ConfigEditorView CreateView(string virtualPath, string physicalPath)
    {
        bool isFile = File.Exists(physicalPath);
        string folder = isFile ? Path.GetDirectoryName(physicalPath)! : physicalPath;
        string? saved = store.ReadText(folder);

        // A configuration that was saved but doesn't hold up is said so, as the folder is closed to everyone else until it does
        // (the store says why: it also knows what couldn't be migrated)
        string? warning = null;
        if (saved is not null && store.Get(folder) is { IsInvalid: true } invalid)
            warning = "The saved configuration of this folder is not valid. Until it is fixed, only admins have access to everything under it.\n" + ParseResult<FolderConfig>.Failed(invalid.Errors).ErrorText;

        if (isFile)
        {
            string? body = saved is null ? null : ConfigText.GetFileBody(saved, Path.GetFileName(physicalPath));
            return new ConfigEditorView
            {
                VirtualPath = virtualPath,
                IsFile = true,
                Contents = body ?? ConfigText.DefaultFileBody(),
                DefaultContents = ConfigText.DefaultFileBody(),
                Schema = ConfigSchema.FileBody,
                Warning = warning
            };
        }

        string defaultText = ConfigText.Default(FolderConfigStore.FileNamesOf(folder), migrator.Current);
        return new ConfigEditorView
        {
            VirtualPath = virtualPath,
            IsFile = false,
            Contents = saved ?? defaultText,
            DefaultContents = defaultText,
            Schema = ConfigSchema.Folder,
            Warning = warning
        };
    }

    /// <summary>Saves the text in the body. Answers with the text as it is saved, which the editor then shows.</summary>
    public async Task<IActionResult> SaveAsync(HttpRequest request, string physicalPath)
    {
        string? sent = await ReadBodyAsync(request.Body);
        if (sent is null)
            return Failed(StatusCodes.Status413PayloadTooLarge, "The configuration is too long.");

        // The editor's lines end the way the browser's platform does. The file always has the same, so the entries that are added to it fit in.
        string text = sent.ReplaceLineEndings("\n");

        // Entries are added and removed as files come and go, so what is read here and written back must not be changed in between
        using (await structureLock.EnterAsync())
        {
            try
            {
                if (Directory.Exists(physicalPath))
                    return SaveFolder(physicalPath, text);
                if (File.Exists(physicalPath))
                    return SaveFile(physicalPath, text);

                return new NotFoundObjectResult("Path not found.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogError(e, "Could not save the configuration for {path}", physicalPath);
                return Failed(StatusCodes.Status500InternalServerError, "The configuration could not be saved: the server can't write it.");
            }
        }
    }

    private IActionResult SaveFolder(string folder, string text)
    {
        IReadOnlySet<string> files = FolderConfigStore.FileNamesOf(folder);

        // What was sent may be an older version's (pasted in, say): it is brought up to date, and then checked as the current version
        MigrationResult migrated = migrator.Migrate(text);
        if (migrated.Error is not null)
            return Failed(StatusCodes.Status400BadRequest, ParseResult<FolderConfig>.Failed([migrated.Error]).ErrorText);

        // Without a version it can't be told which one it is written for, and guessing would apply migrations to what may be up to date already
        if (migrated.From == ConfigVersions.Zero)
        {
            List<string> errors = [.. PermissionParser.ParseFolder(text, files).Errors];
            if (errors.Count == 0)
                errors.Add($"version: is required. Write \"{PermissionParser.VersionKey}\": \"{migrator.Current}\" (the version of this app's format) as the first property.");
            return Failed(StatusCodes.Status400BadRequest, ParseResult<FolderConfig>.Failed(errors).ErrorText);
        }

        text = migrated.Text;
        ParseResult<FolderConfig> parsed = PermissionParser.ParseFolder(text, files);
        if (!parsed.IsValid)
            return Failed(StatusCodes.Status400BadRequest, parsed.ErrorText);

        // Every file has an entry: the ones the text lacks (files that came in from outside the app) get one that changes nothing
        foreach (string name in files.Order(StringComparer.OrdinalIgnoreCase).Where(name => parsed.Value!.FindFile(name) is null))
            text = ConfigText.AddFile(text, name) ?? text;

        // What was added must not have broken it: a configuration that doesn't hold up closes the folder to everyone but admins
        ParseResult<FolderConfig> completed = PermissionParser.ParseFolder(text, files);
        if (!completed.IsValid)
        {
            logger.LogError("Adding the missing file entries made the configuration of {folder} invalid:\n{errors}", folder, completed.ErrorText);
            return Failed(StatusCodes.Status500InternalServerError, "The entries for the files of this folder could not be added to what was sent, so nothing was saved.\n" + completed.ErrorText);
        }

        store.WriteText(folder, text);
        return Saved(text);
    }

    private IActionResult SaveFile(string physicalPath, string text)
    {
        ParseResult<PermsBlock> parsed = PermissionParser.ParseFileBody(text);
        if (!parsed.IsValid)
            return Failed(StatusCodes.Status400BadRequest, parsed.ErrorText);

        // The entry goes into the configuration of the folder (made now if it has none), whose other entries and comments stay
        string folder = Path.GetDirectoryName(physicalPath)!;
        string name = Path.GetFileName(physicalPath);
        string folderText = store.ReadText(folder) ?? ConfigText.Default(FolderConfigStore.FileNamesOf(folder), migrator.Current);
        string? updated = ConfigText.SetFileBody(folderText, name, text);
        if (updated is null)
            return Failed(StatusCodes.Status409Conflict, "The configuration of the folder this file is in can't be edited, as it is not valid. The permissions of the folder have to be fixed first.");

        // What it is now must hold up as a whole. (Not whether every entry has a file: that is the folder's to say when it is saved.)
        ParseResult<FolderConfig> whole = PermissionParser.ParseFolder(updated, existingFiles: null);
        if (!whole.IsValid)
            return Failed(StatusCodes.Status409Conflict, "The configuration of the folder this file is in is not valid. The permissions of the folder have to be fixed first.\n" + whole.ErrorText);

        store.WriteText(folder, updated);
        return Saved(ConfigText.GetFileBody(updated, name) ?? text);
    }

    private static ContentResult Saved(string text)
        => new() { Content = text, ContentType = "text/plain; charset=utf-8" };

    private static ObjectResult Failed(int statusCode, string message)
        => new(message) { StatusCode = statusCode };

    /// <summary>The body as text, or null if it is longer than any configuration may be.</summary>
    private static async Task<string?> ReadBodyAsync(Stream body)
    {
        using StreamReader reader = new(body, Encoding.UTF8);
        StringBuilder text = new();
        char[] buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            _ = text.Append(buffer, 0, read);
            if (text.Length > PermissionParser.MaxLength)
                return null;
        }

        return text.ToString();
    }
}
