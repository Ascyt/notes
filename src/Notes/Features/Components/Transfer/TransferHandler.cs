using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Components.Selection;
using Notes.Features.EntryActions;

namespace Notes.Features.Components.Transfer;

/// <summary>Body of a transfer request: the version of the selection the page showed when the user clicked.</summary>
public sealed record TransferRequest(long Version);

/// <summary>
/// Answers <c>POST ?transfer=copy</c> and <c>?transfer=move</c>: puts the user's selection into the directory the request is for.
/// <c>?transfer=delete</c> deletes the selection instead (the directory plays no part).
/// Answers when it is all done; the selection is cleared after a success and kept after a failure. A success answers
/// <c>{ "leftOutUnreadable": bool }</c>: whether a copy left out entries the user may not read.
/// </summary>
public sealed class TransferHandler(ISelectionService selection, ITransferService transfer, TransferTexts texts, EntryTexts entryTexts,
    ZippedFolders.ArchivePasswordPrompt passwordPrompt, Passwords.PasswordLocks passwordLocks, Passwords.PasswordGate passwordGate,
    Helpers.IPathService path) : IEntryPostAction
{
    public const string TransferParameter = "transfer";

    public string Parameter => TransferParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> PostAsync(EntryRequest entryRequest)
    {
        (HttpRequest request, ClaimsPrincipal user, string physicalDirectory, string kindName) = (entryRequest.Request, entryRequest.User,
            entryRequest.PhysicalPath, entryRequest.Value);
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
                return new BadRequestObjectResult(entryTexts.InvalidRequest);
        }

        if (!Directory.Exists(physicalDirectory))
            return new NotFoundObjectResult(entryTexts.DirectoryNotFound);

        // Without an id there is no selection (and the cookie scheme would answer a Forbid with a redirect to a page that doesn't exist)
        string? userId = SelectionHandler.UserIdOf(user);
        if (userId is null)
            return new ObjectResult(texts.LoginRequired) { StatusCode = StatusCodes.Status403Forbidden };

        // ReadFromJsonAsync throws something other than a JsonException for a body that isn't sent as JSON
        if (!request.HasJsonContentType())
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        TransferRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<TransferRequest>();
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(entryTexts.InvalidRequest);
        }

        if (body is null)
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        // What a password locks for this browser can't be copied, moved or deleted (the transfer would refuse it as unreadable): its password is
        // asked for first, and the request sent again. Before the selection is taken, whose version a request sent again still has to match.
        foreach (string selected in selection.Get(userId).Paths)
            if (path.ToPhysicalPath(selected) is { } physical && passwordLocks.FirstLocked(physical) is { } locked)
                return passwordGate.Refusal(request.HttpContext, locked, physical);

        // Takes the selection (so that no other tab can change it or transfer it as well) only if it is the one the page showed
        TransferBegin begin = selection.TryBeginTransfer(userId, body.Version);
        switch (begin.Outcome)
        {
            case TransferBeginOutcome.Busy:
                return new ConflictObjectResult(texts.AlreadyInProgress);
            case TransferBeginOutcome.Changed:
                return new ConflictObjectResult(texts.SelectionChanged);
            case TransferBeginOutcome.Empty:
                return new BadRequestObjectResult(texts.NothingSelected);
        }

        TransferResult result = new(StatusCodes.Status500InternalServerError, texts.FailedUnexpectedly);
        try
        {
            result = await transfer.RunAsync(user, kind, physicalDirectory, begin.Paths, progress => selection.ReportProgress(userId, progress));
        }
        finally
        {
            selection.EndTransfer(userId, clear: result.IsSuccess);
        }

        if (result.PasswordNeeded is { } passwordNeeded)
            return passwordPrompt.Ask(request.HttpContext, passwordNeeded, physicalDirectory);
        return result.IsSuccess
            ? new JsonResult(new { result.LeftOutUnreadable })
            : new ObjectResult(result.Message) { StatusCode = result.StatusCode };
    }
}
