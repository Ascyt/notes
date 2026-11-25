using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.Passwords;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// <c>POST /entry?zip</c> (the archive button of the navbar): zips every archived folder that is unzipped now and that the user may open,
/// without waiting for them to be idle. Answered at once, as zipping a big one takes long; they are zipped meanwhile (each once nothing uses
/// it). The answer is <c>{ "location": "…" }</c>: the folder above the outermost of them that the entry is in, where the page goes (going
/// into it would unzip it again), or null to stay. The <see cref="ArchiveGate"/> holds no use of anything for it.
/// </summary>
public sealed class ZipHandler(ArchivedFolders archives, AccessTree tree, PasswordLocks passwordLocks, EntryLocations locations,
    ILogger<ZipHandler> logger) : IEntryPostAction
{
    public const string ZipParameter = "zip";

    public string Parameter => ZipParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    /// <summary>Zipping hands nothing out, and only takes folders the user may open anyway.</summary>
    public bool PassesPasswordLocks => true;

    /// <summary>
    /// The unzipped folders that the user may zip with the button: those they could have unzipped (see <see cref="ArchiveGate"/>). Asked of
    /// the unzipped copy, which is what is at the folder's place now (the folder itself is hidden by it).
    /// </summary>
    public static List<string> ZippableFor(ClaimsPrincipal user, ArchivedFolders archives, AccessTree tree, PasswordLocks passwordLocks)
        => [.. archives.UnzippedFolders().Where(folder => archives.MountedAt(folder) is { } copy
            && tree.CanOpen(user, copy.PhysicalPath) && passwordLocks.FirstLocked(copy.PhysicalPath) is null)];

    /// <summary><c>POST /folder?zip=entry</c> (the zip button of the toolbar): only that archived folder, and those unzipped inside it.</summary>
    public const string EntryValue = "entry";

    public Task<IActionResult> PostAsync(EntryRequest request)
    {
        if (request.Value is not ("" or EntryValue))
            return Task.FromResult<IActionResult>(new BadRequestResult());

        List<string> folders = ZippableFor(request.User, archives, tree, passwordLocks);
        if (request.Value == EntryValue)
        {
            string folder = archives.OuterOf(request.PhysicalPath);
            if (archives.MountedAt(folder) is null)
                return Task.FromResult<IActionResult>(new BadRequestResult());
            folders = [.. folders.Where(unzipped => locations.IsSameOrInside(unzipped, folder)
                                                    || locations.IsSameOrInside(unzipped, archives.MountedAt(folder)!.PhysicalPath))];
        }

        // Where the page goes, found before zipping: the outermost of them that the entry is in (they are by virtual path the same, zipped or not)
        string? outermost = folders.Where(folder => locations.IsSameOrInside(request.PhysicalPath, folder))
            .MinBy(folder => locations.ToVirtualPath(folder).Length);
        string? location = outermost is not null && locations.ParentOf(outermost) is { } above
            ? VirtualPaths.ToAddress(locations.ToVirtualPath(above))
            : null;

        // Apart from the request: zipping waits for what uses them, and takes a while for a big one. One that can't be zipped now stays
        // unzipped (and counts, as before) until the job that zips idle ones gets to it. Not through Task.Run: the first one takes its gate
        // before this answers, so the page that is loaded next already shows it being zipped.
        _ = ZipAllAsync(folders);

        return Task.FromResult<IActionResult>(new OkObjectResult(new { location }));
    }

    private async Task ZipAllAsync(List<string> folders)
    {
        foreach (string folder in folders)
        {
            try
            {
                if (!await archives.ZipAsync(folder, stopsWhenOpened: true))
                    logger.LogInformation("The archived folder {folder} stays unzipped for now: it is in use, was opened, or zipping failed",
                        folder);
            }
            catch (Exception e)
            {
                // (Nobody awaits this: nothing may end up unobserved)
                logger.LogError(e, "Zipping the archived folder {folder} failed; it stays unzipped", folder);
            }
        }
    }
}
