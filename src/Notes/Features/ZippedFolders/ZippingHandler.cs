using Microsoft.AspNetCore.Mvc;
using Notes.Features.EntryActions;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// <c>GET /folder?zipping</c>: how the archived folder is kept now, for the bar of the listing while it is zipped (_Unzipping.js asks until it
/// is no longer being zipped): <c>{ "state": "zipping" | "zipped" | "unzipped" | null, "progress": 0…1, "status": … }</c> (status: what the
/// zipping is doing, see <see cref="Helpers.WorkStatus"/>), null for a folder that isn't archived. The <see cref="ArchiveGate"/> holds no use of it and unzips nothing for it: asking must not stop the zipping it asks about.
/// </summary>
public sealed class ZippingHandler(ArchivedFolders archives, Auth.AccessTree tree, Passwords.PasswordLocks passwordLocks) : IEntryGetAction
{
    public const string ZippingParameter = "zipping";

    public string Parameter => ZippingParameter;

    /// <summary>Readable is checked here, as the listing does it (see <see cref="GetAsync"/>).</summary>
    public AccessNeeded Needs => AccessNeeded.Known;

    /// <summary>The listing marks it whether or not its password is unlocked.</summary>
    public bool PassesPasswordLocks => true;

    public Task<IActionResult> GetAsync(EntryRequest request)
    {
        // As the listing, which marks only what the user may read (whether it is unzipped tells whether someone used it lately), also while
        // its password is locked (the lock button zips what it locks): a lock counts as unreadable otherwise
        using (passwordLocks.IgnoringLocks())
            if (!tree.GetAccess(request.User, request.PhysicalPath).CanRead)
                return Task.FromResult<IActionResult>(new StatusCodeResult(StatusCodes.Status403Forbidden));

        ArchiveState? state = Directory.Exists(request.PhysicalPath) ? archives.ArchiveStateOf(request.PhysicalPath) : null;
        double progress = state == ArchiveState.Zipping ? archives.ZipProgressOf(request.PhysicalPath) ?? 0 : 0;
        string? name = state switch
        {
            ArchiveState.Zipping => "zipping",
            ArchiveState.Zipped => "zipped",
            ArchiveState.Unzipped => "unzipped",
            _ => null
        };
        Helpers.WorkStatus? status = state == ArchiveState.Zipping ? archives.ZipStatusOf(request.PhysicalPath) : null;
        return Task.FromResult<IActionResult>(new JsonResult(new { state = name, progress, status }));
    }
}
