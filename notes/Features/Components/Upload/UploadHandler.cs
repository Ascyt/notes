using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Helpers;
using notes.Features.History;

namespace notes.Features.Components.Upload;

/// <summary>
/// Answers <c>?upload</c>: <c>?upload=start</c> returns an id made by the server, then the file follows as a sequence of raw chunks
/// (<c>?upload=&lt;id&gt;&amp;name=..&amp;offset=..&amp;last=..</c>) so that neither memory nor the request size limits of the server
/// or proxies cap the file size. <c>?upload=&lt;id&gt;&amp;abort</c> discards an upload.
/// </summary>
public sealed class UploadHandler(IPathService path, IUploadService uploadService, AccessTree tree, HistoryService history)
{
    public async Task<IActionResult> HandleAsync(HttpRequest request, ClaimsPrincipal user, string physicalPath, string upload)
    {
        if (!Directory.Exists(physicalPath))
            return new NotFoundObjectResult("Directory not found.");
        // Uploading into a directory the user may not read would tell them which names are taken in it
        if (!tree.GetAccess(user, physicalPath).CanRead)
            return Forbidden("No read access to this directory.");

        // The user's own folder holds their uploads, so the id means nothing to anyone else
        string? userId = user.FindFirst("sub")?.Value;
        // (Not a Forbid: the cookie scheme would answer it with a redirect to a page that doesn't exist)
        if (userId is null)
            return Forbidden("Login required to upload files.");

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

        // Asked early (with every chunk) so that a file that can't be put here isn't uploaded in vain, and once more for the name the
        // finished file actually gets
        bool MayCreate(string physical) => tree.GetAccess(user, physical).CanCreate;
        if (!MayCreate(Path.Combine(physicalPath, name)))
            return Forbidden($"Insufficient permissions to create '{name}' here.");

        string? result;
        try
        {
            result = await uploadService.UploadChunkAsync(userId, uploadId, physicalPath, name, offset, request.Query["last"].Count > 0, request.Body, MayCreate);
        }
        catch (AccessDeniedException e)
        {
            return Forbidden(e.Message);
        }

        if (result is null)
            return new ConflictObjectResult("No such upload, or the chunk does not continue it.");

        // The last chunk gave the file its place (the path of it), and an entry in the configuration of the directory
        if (result != "")
            history.AfterChange(user, [Path.Combine(physicalPath, result.Split('/')[^1]), Path.Combine(physicalPath, PathService.ConfigFileName)], HistoryChangeKind.Change);

        return new ContentResult { Content = result };
    }

    private static ObjectResult Forbidden(string message)
        => new(message) { StatusCode = StatusCodes.Status403Forbidden };
}
