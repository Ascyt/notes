using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Addresses;
using Notes.Features.Auth;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.Components.Upload;

/// <summary>
/// Answers <c>?upload</c>: <c>?upload=start</c> returns an id made by the server, then the file follows as a sequence of raw chunks
/// (<c>?upload=&lt;id&gt;&amp;name=..&amp;offset=..&amp;last=..</c>) so that neither memory nor the request size limits of the server
/// or proxies cap the file size. <c>?upload=&lt;id&gt;&amp;abort</c> discards an upload.
/// </summary>
public sealed class UploadHandler(IPathService path, IUploadService uploadService, AccessTree tree, LimitsConfig limits, HistoryService history,
    AppAddresses appAddresses, UploadTexts texts, EntryTexts entryTexts, NameTexts nameTexts) : IEntryPostAction
{
    public const string UploadParameter = "upload";

    public string Parameter => UploadParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> PostAsync(EntryRequest entryRequest)
    {
        (HttpRequest request, ClaimsPrincipal user, string physicalPath, string upload) = (entryRequest.Request, entryRequest.User,
            entryRequest.PhysicalPath, entryRequest.Value);
        if (!Directory.Exists(physicalPath))
            return new NotFoundObjectResult(entryTexts.DirectoryNotFound);
        // Uploading into a directory the user may not read would tell them which names are taken in it
        if (!tree.GetAccess(user, physicalPath).CanRead)
            return Forbidden(entryTexts.NoReadAccessToDirectory);

        // The user's own folder holds their uploads, so the id means nothing to anyone else
        string? userId = user.FindFirst("sub")?.Value;
        // (Not a Forbid: the cookie scheme would answer it with a redirect to a page that doesn't exist)
        if (userId is null)
            return Forbidden(texts.LoginRequired);

        if (upload == "start")
        {
            try
            {
                return new ContentResult { Content = uploadService.StartUpload(userId) };
            }
            catch (UploadRefusedException e)
            {
                return new ObjectResult(e.Message) { StatusCode = e.StatusCode };
            }
        }

        if (!Guid.TryParse(upload, out Guid uploadId))
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        if (request.Query["abort"].Count > 0)
        {
            uploadService.AbortUpload(userId, uploadId);
            return new OkResult();
        }

        if (NewNames.Check(request.Query["name"].ToString(), out string name) is { } invalid)
            return new BadRequestObjectResult(nameTexts.Problem(invalid));
        if (!long.TryParse(request.Query["offset"], out long offset) || offset < 0)
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        // Asked early (with every chunk) so that a file that can't be put here isn't uploaded in vain, and once more for the name the
        // finished file actually gets, under the lock (the directory may have changed since this chunk was checked)
        bool MayCreate(string physical) => tree.GetAccess(user, physicalPath).CanRead && tree.GetAccess(user, physical).CanCreate;
        if (!MayCreate(Path.Combine(physicalPath, name)))
            return Forbidden(texts.CantCreate(name));
        string newVirtualPath = path.ToVirtualPath(Path.Combine(physicalPath, name));
        if (limits.IsTooLong(newVirtualPath))
            return new BadRequestObjectResult(nameTexts.PathTooLong(newVirtualPath, limits.MaxPathBytes));
        // (At the root, where the pages of the app may be. The name the file gets if this one is taken, "name (1)", is no address of the app.)
        if (appAddresses.TakesNewEntry(newVirtualPath))
            return new BadRequestObjectResult(nameTexts.TakenByTheApp(name));

        string? result;
        try
        {
            result = await uploadService.UploadChunkAsync(userId, uploadId, physicalPath, name, offset, request.Query["last"].Count > 0,
                request.Body, MayCreate);
        }
        catch (AccessDeniedException e)
        {
            return Forbidden(e.Message);
        }
        catch (UploadRefusedException e)
        {
            return new ObjectResult(e.Message) { StatusCode = e.StatusCode };
        }

        if (result is null)
            return new ConflictObjectResult(texts.NoSuchUpload);

        // The last chunk gave the file its place (the path of it), and an entry in the configuration of the directory
        if (result != "")
            history.AfterChange(user, [Path.Combine(physicalPath, result.Split('/')[^1]), Path.Combine(physicalPath,
                PathService.ConfigFileName)], HistoryChangeKind.Change);

        return new ContentResult { Content = result };
    }

    private static ObjectResult Forbidden(string message)
        => new(message) { StatusCode = StatusCodes.Status403Forbidden };
}
