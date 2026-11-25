using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.Configuration;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Upload;

/// <summary>
/// <c>GET /folder?unzipping=name</c>: how unzipping the uploaded archive of that name in the folder goes, for its bar in the listing, which asks
/// until it ended (asked of the folder, as the archive is gone once it is unzipped):
/// <c>{ "state": "unzipping" | "unzipped" | "failed" | null, "progress": 0…1, "folder": …, "leftOut": n, "error": … }</c>. Null: it isn't
/// being unzipped (nor was it lately).
/// </summary>
public sealed class UnzippingUploadHandler(IPathService path, AccessTree tree, FolderConfigStore configs, UploadedArchiveUnzipper unzipper)
    : IEntryGetAction
{
    public const string UnzippingParameter = "unzipping";

    public string Parameter => UnzippingParameter;
    public AccessNeeded Needs => AccessNeeded.Readable;

    public Task<IActionResult> GetAsync(EntryRequest request)
    {
        string name = request.Value;
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name is "." or ".." || !Directory.Exists(request.PhysicalPath))
            return Task.FromResult<IActionResult>(new BadRequestResult());
        // (Gone once it is unzipped: then where it was)
        string archive = path.ToPhysicalPath(request.VirtualPath == "" ? name : request.VirtualPath + "/" + name)
                         ?? Path.Combine(request.PhysicalPath, name);
        if (File.Exists(archive) && !tree.GetAccess(request.User, archive).CanRead)
            return Task.FromResult<IActionResult>(new StatusCodeResult(StatusCodes.Status403Forbidden));

        if (unzipper.ProgressOf(archive) is { } progress)
            return Answer("unzipping", progress);
        if (unzipper.OutcomeOf(archive) is { } outcome)
            return outcome.Error is null
                ? Answer("unzipped", 1, folder: outcome.FolderVirtualPath, leftOut: outcome.LeftOut)
                : Answer("failed", 0, error: outcome.Error);

        // Not in memory (the app started again since): what its entry says
        FileEntry? entry = File.Exists(archive) ? configs.Get(Path.GetDirectoryName(archive)!).Config?.FindFile(Path.GetFileName(archive)) : null;
        if (entry?.UnzipPending == true)
        {
            unzipper.EnsureRunning(archive);
            return Answer("unzipping", 0);
        }
        return entry?.UnzipError is { } error ? Answer("failed", 0, error: error) : Answer(null, 0);
    }

    private static Task<IActionResult> Answer(string? state, double progress, string? folder = null, int leftOut = 0, string? error = null)
        => Task.FromResult<IActionResult>(new JsonResult(new { state, progress, folder, leftOut, error }));
}
