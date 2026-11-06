using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Components.Transfer;

namespace notes.Features.Components.Delete;

/// <summary>
/// Answers a DELETE request: removes the file, or the directory and everything in it. It is a delete of a one-entry selection, so it is
/// checked and done exactly like one (all or nothing; nothing hidden from the user may be inside).
/// </summary>
public sealed class DeleteHandler(ITransferService transfer)
{
    public async Task<IActionResult> HandleAsync(ClaimsPrincipal user, string virtualPath, string physicalPath)
    {
        if (virtualPath == "")
            return new BadRequestObjectResult("Cannot delete the root directory.");

        // The target directory plays no part in a delete
        TransferResult result = await transfer.RunAsync(user, TransferKind.Delete, physicalPath, [virtualPath]);
        return result.IsSuccess
            ? new OkResult()
            : new ObjectResult(result.Message) { StatusCode = result.StatusCode };
    }
}
