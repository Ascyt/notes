using Microsoft.AspNetCore.Mvc;
using Notes.Features.EntryActions;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// <c>GET /folder?zipping</c>: how the archived folder is kept now, for the bar of the listing while it is zipped (_Unzipping.js asks until it
/// is no longer being zipped): <c>{ "state": "zipping" | "zipped" | "unzipped" | null, "progress": 0…1 }</c>, null for a folder that isn't
/// archived. The <see cref="ArchiveGate"/> holds no use of it and unzips nothing for it: asking must not stop the zipping it asks about.
/// </summary>
public sealed class ZippingHandler(ArchivedFolders archives) : IEntryGetAction
{
    public const string ZippingParameter = "zipping";

    public string Parameter => ZippingParameter;

    /// <summary>
    /// As the listing, which marks only what the user may read: whether it is unzipped tells whether someone used it lately.
    /// </summary>
    public AccessNeeded Needs => AccessNeeded.Readable;

    /// <summary>The listing marks it whether or not its password is unlocked.</summary>
    public bool PassesPasswordLocks => true;

    public Task<IActionResult> GetAsync(EntryRequest request)
    {
        ArchiveState? state = Directory.Exists(request.PhysicalPath) ? archives.ArchiveStateOf(request.PhysicalPath) : null;
        double progress = state == ArchiveState.Zipping ? archives.ZipProgressOf(request.PhysicalPath) ?? 0 : 0;
        string? name = state switch
        {
            ArchiveState.Zipping => "zipping",
            ArchiveState.Zipped => "zipped",
            ArchiveState.Unzipped => "unzipped",
            _ => null
        };
        return Task.FromResult<IActionResult>(new JsonResult(new { state = name, progress }));
    }
}
