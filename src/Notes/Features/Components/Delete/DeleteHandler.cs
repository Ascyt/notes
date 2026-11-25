using Microsoft.AspNetCore.Mvc;
using Notes.Features.Components.Transfer;
using Notes.Features.EntryActions;

namespace Notes.Features.Components.Delete;

/// <summary>
/// Answers a DELETE request: removes the file, or the directory and everything in it. It is a delete of a one-entry selection, so it is
/// checked and done exactly like one (all or nothing; nothing hidden from the user may be inside).
/// </summary>
public sealed class DeleteHandler(ITransferService transfer, EntryTexts entryTexts, ZippedFolders.ArchivePasswordPrompt passwordPrompt)
    : IEntryDeleteAction
{
    /// <summary>The DELETE of the entry itself, without a parameter.</summary>
    public string? Parameter => null;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> DeleteAsync(EntryRequest request)
    {
        if (request.IsRoot)
            return new BadRequestObjectResult(entryTexts.RootCantBeDeleted);

        // The target directory plays no part in a delete
        TransferResult result = await transfer.RunAsync(request.User, TransferKind.Delete, request.PhysicalPath, [request.VirtualPath]);
        if (result.PasswordNeeded is { } passwordNeeded)
            return passwordPrompt.Ask(request.Context, passwordNeeded, request.PhysicalPath);
        return result.IsSuccess
            ? new OkResult()
            : new ObjectResult(result.Message) { StatusCode = result.StatusCode };
    }
}
