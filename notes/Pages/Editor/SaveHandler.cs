using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Helpers;
using notes.Features.History;

namespace notes.Pages.Editor;

/// <summary>Answers the editor's save: a POST whose body is the new contents of the file. Needs write access to the file.</summary>
/// <remarks>
/// The body is read first, as the client may send it slowly. The check and the write then happen under the <see cref="StructureLock"/>, so
/// that the file can't be renamed, moved or deleted in between: the save would otherwise make a new file where it was, without asking whether
/// one may be created there.
/// </remarks>
public sealed class SaveHandler(Config config, AccessTree tree, StructureLock structureLock, HistoryService history)
{
    public async Task<IActionResult> HandleAsync(ClaimsPrincipal user, string physicalPath, Stream body)
    {
        // Asked before the body is read, so that it isn't read in vain (and again below, where it counts)
        if (!File.Exists(physicalPath))
            return new NotFoundObjectResult("File not found.");
        if (!tree.GetAccess(user, physicalPath).CanWrite)
            return Forbidden();

        string? contents = await ReadAtMostAsync(body, config.Limits.MaxTextBytes);
        if (contents is null)
            return new ObjectResult($"The text is too long to be saved (at most {FileView.FormatSize(config.Limits.MaxTextBytes)}).") { StatusCode = StatusCodes.Status413PayloadTooLarge };

        using (await structureLock.EnterAsync())
        {
            if (!File.Exists(physicalPath))
                return new NotFoundObjectResult("The file was renamed, moved or deleted in the meantime. Nothing was saved.");
            if (!tree.GetAccess(user, physicalPath).CanWrite)
                return Forbidden();

            history.BeforeChange(user, [physicalPath], contents);
            // Only ever writes into the file that is there (never makes one)
            await using (FileStream file = new(physicalPath, FileMode.Truncate, FileAccess.Write, FileShare.Read))
            await using (StreamWriter writer = new(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                await writer.WriteAsync(contents);
            history.AfterChange(user, [physicalPath], HistoryChangeKind.Save);
        }

        return new OkResult();
    }

    private static ObjectResult Forbidden()
        => new("Insufficient permissions to change this file.") { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>The body as UTF-8 text, or null if it is more than <paramref name="maxBytes"/> bytes.</summary>
    public static async Task<string?> ReadAtMostAsync(Stream body, int maxBytes)
    {
        using MemoryStream bytes = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer)) > 0)
        {
            if (bytes.Length + read > maxBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }
}
