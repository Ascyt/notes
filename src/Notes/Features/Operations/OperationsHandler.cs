using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;

namespace Notes.Features.Operations;

/// <summary>Body of a cancel: the id of the operation.</summary>
public sealed record CancelOperationRequest(Guid Id);

/// <summary>
/// <c>GET /?operations</c>: the storage operations running now that the user may see (<see cref="StorageOperations.VisibleTo"/>), the most
/// recent first, for the list of the navbar button (_Operations.js): <c>[ { id, title, directory, user, progress, status, canCancel,
/// cancelling } ]</c>. <c>POST /?operations=cancel</c> with <c>{ "id": … }</c> cancels one, if the user may (who started it, and admins).
/// Asked of any entry; nobody who isn't logged in has any (404).
/// </summary>
public sealed class OperationsHandler(StorageOperations operations, AccessTree tree, OperationTexts texts, EntryTexts entryTexts)
    : IEntryGetAction, IEntryPostAction
{
    public const string OperationsParameter = "operations";
    public const string CancelValue = "cancel";

    public string Parameter => OperationsParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    /// <summary>The list says no more than where something runs, which those who may read it know of anyway.</summary>
    public bool PassesPasswordLocks => true;

    public Task<IActionResult> GetAsync(EntryRequest request)
    {
        if (request.User.Identity?.IsAuthenticated != true || request.Value != "")
            return Task.FromResult<IActionResult>(new NotFoundResult());
        return Task.FromResult<IActionResult>(new JsonResult(operations.VisibleTo(request.User, tree).Select(operation => new
        {
            id = operation.Id,
            title = texts.Name(operation.Kind),
            directory = DirectoryOf(operation),
            user = operation.UserName ?? texts.TheApp,
            progress = operation.Progress,
            status = operation.Status,
            canCancel = StorageOperations.MayCancel(request.User, operation),
            cancelling = operation.IsCancelled
        })));
    }

    /// <summary>The address of the directory it runs in: the folder itself for one of a folder, the folder a file is in for one of a file.</summary>
    private static string DirectoryOf(StorageOperation operation)
        => VirtualPaths.ToAddress(operation.IsOfFile && operation.VirtualPath.Contains('/')
            ? operation.VirtualPath[..operation.VirtualPath.LastIndexOf('/')]
            : operation.IsOfFile ? "" : operation.VirtualPath);

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        if (request.User.Identity?.IsAuthenticated != true)
            return new NotFoundResult();
        if (request.Value != CancelValue || !request.Request.HasJsonContentType())
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        CancelOperationRequest? body;
        try
        {
            body = await request.Request.ReadFromJsonAsync<CancelOperationRequest>();
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(entryTexts.InvalidRequest);
        }

        // One the user may not see is answered as one that isn't there
        if (body is null || operations.Find(body.Id) is not { } operation
            || !operations.VisibleTo(request.User, tree).Contains(operation))
            return new NotFoundObjectResult(texts.NotFound);
        if (!StorageOperations.MayCancel(request.User, operation))
            return new ObjectResult(texts.MayNotCancel) { StatusCode = StatusCodes.Status403Forbidden };

        operation.Cancel();
        return new OkResult();
    }
}
