using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Helpers;

namespace notes.Features.Components.Upload;

/// <summary>
/// Answers <c>?upload</c>: <c>?upload=start</c> returns an id made by the server, then the file follows as a sequence of raw chunks
/// (<c>?upload=&lt;id&gt;&amp;name=..&amp;offset=..&amp;last=..</c>) so that neither memory nor the request size limits of the server
/// or proxies cap the file size. <c>?upload=&lt;id&gt;&amp;abort</c> discards an upload.
/// </summary>
public sealed class UploadHandler(IPathService path, IUploadService uploadService)
{
    public async Task<IActionResult> HandleAsync(HttpRequest request, ClaimsPrincipal user, string physicalPath, string upload)
    {
        if (!Directory.Exists(physicalPath))
            return new NotFoundObjectResult("Directory not found.");

        // The user's own folder holds their uploads, so the id means nothing to anyone else
        string? userId = user.FindFirst("sub")?.Value;
        if (userId is null)
            return new ForbidResult();

        if (upload == "start")
            return new ContentResult { Content = uploadService.StartUpload(userId) };

        if (!Guid.TryParse(upload, out Guid uploadId))
            return new BadRequestObjectResult("Invalid upload id.");

        if (request.Query["abort"].Count > 0)
        {
            uploadService.AbortUpload(userId, uploadId);
            return new OkResult();
        }

        string name = request.Query["name"].ToString();
        if (!path.IsValidName(name))
            return new BadRequestObjectResult("Invalid file name.");
        if (!long.TryParse(request.Query["offset"], out long offset) || offset < 0)
            return new BadRequestObjectResult("Invalid offset.");

        string? result = await uploadService.UploadChunkAsync(userId, uploadId, physicalPath, name, offset, request.Query["last"].Count > 0, request.Body);
        return result is null
            ? new ConflictObjectResult("No such upload, or the chunk does not continue it.")
            : new ContentResult { Content = result };
    }
}
