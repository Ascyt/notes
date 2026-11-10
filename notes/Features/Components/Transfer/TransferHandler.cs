using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Components.Selection;

namespace notes.Features.Components.Transfer;

/// <summary>Body of a transfer request: the version of the selection the page showed when the user clicked.</summary>
public sealed record TransferRequest(long Version);

/// <summary>
/// Answers <c>POST ?transfer=copy</c> and <c>?transfer=move</c>: puts the user's selection into the directory the request is for.
/// <c>?transfer=delete</c> deletes the selection instead (the directory plays no part).
/// Answers when it is all done; the selection is cleared after a success and kept after a failure. A success answers
/// <c>{ "leftOutUnreadable": bool }</c>: whether a copy left out entries the user may not read.
/// </summary>
public sealed class TransferHandler(ISelectionService selection, ITransferService transfer)
{
    public async Task<IActionResult> HandleAsync(HttpRequest request, ClaimsPrincipal user, string physicalDirectory, string kindName)
    {
        TransferKind kind;
        switch (kindName)
        {
            case "copy":
                kind = TransferKind.Copy;
                break;
            case "move":
                kind = TransferKind.Move;
                break;
            case "delete":
                kind = TransferKind.Delete;
                break;
            default:
                return new BadRequestObjectResult("Invalid transfer kind.");
        }

        if (!Directory.Exists(physicalDirectory))
            return new NotFoundObjectResult("Directory not found.");

        // Without an id there is no selection (and the cookie scheme would answer a Forbid with a redirect to a page that doesn't exist)
        string? userId = SelectionHandler.UserIdOf(user);
        if (userId is null)
            return new ObjectResult("Login required to copy, move or delete.") { StatusCode = StatusCodes.Status403Forbidden };

        // ReadFromJsonAsync throws something other than a JsonException for a body that isn't sent as JSON
        if (!request.HasJsonContentType())
            return new BadRequestObjectResult("Invalid request.");

        TransferRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<TransferRequest>();
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid request.");
        }

        if (body is null)
            return new BadRequestObjectResult("Invalid request.");

        // Takes the selection (so that no other tab can change it or transfer it as well) only if it is the one the page showed
        TransferBegin begin = selection.TryBeginTransfer(userId, body.Version);
        switch (begin.Outcome)
        {
            case TransferBeginOutcome.Busy:
                return new ConflictObjectResult("A copy or move is already in progress.");
            case TransferBeginOutcome.Changed:
                return new ConflictObjectResult("The selection was changed in the meantime (in another tab?). Nothing was done.");
            case TransferBeginOutcome.Empty:
                return new BadRequestObjectResult("Nothing is selected.");
        }

        TransferResult result = new(StatusCodes.Status500InternalServerError, "The transfer failed unexpectedly.");
        try
        {
            result = await transfer.RunAsync(user, kind, physicalDirectory, begin.Paths, progress => selection.ReportProgress(userId, progress));
        }
        finally
        {
            selection.EndTransfer(userId, clear: result.IsSuccess);
        }

        return result.IsSuccess
            ? new JsonResult(new { result.LeftOutUnreadable })
            : new ObjectResult(result.Message) { StatusCode = result.StatusCode };
    }
}
