using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.EntryActions;
using notes.Features.Helpers;
using notes.Features.History;

namespace notes.Features.Editor;

/// <summary>Answers the editor's save: a POST whose body is the new contents of the file. Needs write access to the file.</summary>
/// <remarks>
/// The body is read first, as the client may send it slowly. The check and the write then happen under the <see cref="StructureLock"/>, so
/// that the file can't be renamed, moved or deleted in between: the save would otherwise make a new file where it was, without asking whether
/// one may be created there. The new contents replace the file in one step, so that whoever reads it meanwhile gets the old text or the new
/// one, never half of it.
/// </remarks>
public sealed class SaveHandler(Config config, NotesLocations locations, AccessTree tree, StructureLock structureLock, HistoryService history) : IEntryPostAction
{
    /// <summary>Refuses what isn't UTF-8 instead of replacing it with U+FFFD.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The POST of the file itself, without a parameter.</summary>
    public string? Parameter => null;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        (ClaimsPrincipal user, string physicalPath, Stream body) = (request.User, request.PhysicalPath, request.Request.Body);
        // Asked before the body is read, so that it isn't read in vain (and again below, where it counts)
        if (!File.Exists(physicalPath))
            return new NotFoundObjectResult("File not found.");
        if (!tree.GetAccess(user, physicalPath).CanWrite)
            return Forbidden();

        byte[]? bytes = await TextFiles.ReadAtMostAsync(body, config.Limits.MaxTextBytes);
        if (bytes is null)
            return new ObjectResult($"The text is too long to be saved (at most {ByteSizes.Format(config.Limits.MaxTextBytes)}).") { StatusCode = StatusCodes.Status413PayloadTooLarge };
        if (DecodeOrNull(bytes) is not { } contents)
            return new BadRequestObjectResult("The text isn't valid UTF-8. Nothing was saved.");

        using (await structureLock.EnterAsync())
        {
            if (!File.Exists(physicalPath))
                return new NotFoundObjectResult("The file was renamed, moved or deleted in the meantime. Nothing was saved.");
            if (!tree.GetAccess(user, physicalPath).CanWrite)
                return Forbidden();

            history.BeforeChange(user, [physicalPath], contents);
            if (!await ReplaceContentsAsync(physicalPath, bytes))
                return new ConflictObjectResult("The file is in use and couldn't be replaced. Nothing was saved.");
            history.AfterChange(user, [physicalPath], HistoryChangeKind.Save);
        }

        return new OkResult();
    }

    /// <summary>
    /// Writes the contents into the app's data of the file's location (the root or a bridge, where no listing shows them) and puts them in
    /// place of the file. That is the same volume, so it is a rename. <see cref="File.Replace(string, string, string?)"/> rather than a move:
    /// only it replaces a file on Windows that is open for reading (by a reader that allows it to be deleted, as the app's own do), and it
    /// never makes a file where there is none. False if the file is held open by someone who doesn't let it be replaced (only on Windows).
    /// </summary>
    private async Task<bool> ReplaceContentsAsync(string physicalPath, byte[] bytes)
    {
        string temp = Path.Combine(locations.TempDirectoryFor(physicalPath, "save"), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            await File.WriteAllBytesAsync(temp, bytes);
            try
            {
                File.Replace(temp, physicalPath, destinationBackupFileName: null);
            }
            // (A failure to write the temporary file, such as a full disk, is no conflict and is let through)
            catch (IOException)
            {
                return false;
            }
            return true;
        }
        finally
        {
            // Left behind only if the move didn't happen
            File.Delete(temp);
        }
    }

    private static ObjectResult Forbidden()
        => new("Insufficient permissions to change this file.") { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>The bytes as UTF-8 text, or null if they aren't valid UTF-8.</summary>
    public static string? DecodeOrNull(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
