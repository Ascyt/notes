using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.Components.Create;

/// <summary>
/// Answers <c>?create=file</c> and <c>?create=directory</c>: makes a timestamp-named entry in the directory and redirects to it (a file to its
/// editor).
/// </summary>
public sealed class CreateHandler(EntryLocations locations, IPathService path, AccessTree tree, StructureLock structureLock,
    FolderConfigStore configs, HistoryService history, LimitsConfig limits, TimeProvider time, CreateTexts texts, EntryTexts entryTexts,
    NameTexts nameTexts) : IEntryPostAction
{
    public const string CreateParameter = "create";

    public string Parameter => CreateParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        (ClaimsPrincipal user, string physicalDirectory, string create) = (request.User, request.PhysicalPath, request.Value);
        if (create is not ("file" or "directory"))
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        bool isFile = create == "file";
        string baseName = time.GetLocalNow().ToString("yyyy-MM-dd_HH-mm");
        string extension = isFile ? ".md" : "";

        using (await structureLock.EnterAsync())
        {
            // Checked under the lock: the directory could otherwise be moved, or another made in its place, after it was checked
            if (!Directory.Exists(physicalDirectory))
                return new NotFoundObjectResult(entryTexts.DirectoryNotFound);
            // Creating in a directory the user may not read would tell them which names are taken in it
            if (!tree.GetAccess(user, physicalDirectory).CanRead)
                return new ObjectResult(entryTexts.NoReadAccessToDirectory) { StatusCode = StatusCodes.Status403Forbidden };

            // A name that is taken is skipped (the user can see all names in the directory)
            string name = baseName + extension;
            for (int n = 1; !locations.IsFree(Path.Combine(physicalDirectory, name)); n++)
                name = $"{baseName}_{n}{extension}";

            string newPhysicalPath = Path.Combine(physicalDirectory, name);
            if (!tree.GetAccess(user, newPhysicalPath).CanCreate)
                return new ObjectResult(texts.CantCreateHere(isFile)) { StatusCode = StatusCodes.Status403Forbidden };
            if (limits.IsTooLong(path.ToVirtualPath(newPhysicalPath)))
                return new BadRequestObjectResult(nameTexts.PathTooLong(path.ToVirtualPath(newPhysicalPath), limits.MaxPathBytes));

            // The configuration of the directory gets an entry for a new file
            string[] changed = isFile ? [newPhysicalPath, configs.ConfigPathOf(physicalDirectory)] : [newPhysicalPath];
            history.BeforeChange(user, changed);
            if (isFile)
            {
                await File.WriteAllTextAsync(newPhysicalPath, "");
                configs.FileAdded(physicalDirectory, name);
            }
            else
                _ = Directory.CreateDirectory(newPhysicalPath);
            history.AfterChange(user, changed, HistoryChangeKind.Change);

            // A new file is there to be written: it opens in the editor, not as the (empty) preview of a markdown file
            return new RedirectResult(VirtualPaths.ToAddress(path.ToVirtualPath(newPhysicalPath)) + (isFile ? "?edit" : ""));
        }
    }
}
