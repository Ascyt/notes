using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Selection;

/// <summary>Body of the requests that select or unselect entries.</summary>
public sealed record SelectionRequest(string[]? Paths);

/// <summary>
/// Answers <c>?selection</c>. A GET returns the user's selection; a POST changes it: <c>?selection=add</c> and <c>?selection=remove</c>
/// take the virtual paths in the body, <c>?selection=clear</c> unselects everything. Every answer is the selection as it is afterwards, with
/// what it is like for this browser (<see cref="SelectionView"/>).
/// </summary>
/// <remarks>The selection is of all of the entries, so it doesn't matter which entry it is asked of.</remarks>
public sealed class SelectionHandler(EntryLocations locations, IPathService path, AccessTree tree, ISelectionService selection,
    SelectionTexts texts, EntryTexts entryTexts, Passwords.PasswordLocks passwordLocks, SelectionStatusReader status)
    : IEntryGetAction, IEntryPostAction
{
    /// <summary>More entries than this in one request is not something a page does.</summary>
    private const int MaxPathsPerRequest = 10_000;

    public const string SelectionParameter = "selection";

    public string Parameter => SelectionParameter;
    public AccessNeeded Needs => AccessNeeded.Known;

    /// <summary>The user's id (Keycloak's "sub" claim), or null if there is none. The selection is stored under it.</summary>
    public static string? UserIdOf(ClaimsPrincipal user)
        => user.FindFirst("sub")?.Value;

    /// <summary>
    /// Whether the user may read the root, without which they have no selection. It is the permission that counts, as for the
    /// directories above an exposed one, whether the site shows the root or not.
    /// </summary>
    private bool MayReadTheRoot(ClaimsPrincipal user)
        => tree.GetAccess(user, locations.Root.PhysicalPath).CanRead;

    public Task<IActionResult> GetAsync(EntryRequest request)
    {
        string? userId = UserIdOf(request.User);
        if (userId is null || !MayReadTheRoot(request.User))
            return Task.FromResult<IActionResult>(new NotFoundResult());

        return Task.FromResult<IActionResult>(Answer(selection.Get(userId)));
    }

    public async Task<IActionResult> PostAsync(EntryRequest entryRequest)
    {
        (HttpRequest request, ClaimsPrincipal user, string action) = (entryRequest.Request, entryRequest.User, entryRequest.Value);
        string? userId = UserIdOf(user);
        if (userId is null || !MayReadTheRoot(user))
            return new NotFoundResult();

        SelectionSnapshot? result;
        switch (action)
        {
            case "clear":
                result = selection.Clear(userId);
                break;

            case "add" or "remove":
                // ReadFromJsonAsync throws something other than a JsonException for a body that isn't sent as JSON
                if (!request.HasJsonContentType())
                    return new BadRequestObjectResult(entryTexts.InvalidRequest);

                SelectionRequest? body;
                try
                {
                    body = await request.ReadFromJsonAsync<SelectionRequest>();
                }
                catch (JsonException)
                {
                    return new BadRequestObjectResult(entryTexts.InvalidRequest);
                }

                if (body?.Paths is not { } paths || paths.Length > MaxPathsPerRequest)
                    return new BadRequestObjectResult(entryTexts.InvalidRequest);

                // Paths are stored the way the listing shows them. Unselecting needs no checks (the entry may be gone by now),
                // and entries that can't be selected (gone, hidden, may not be opened) are left out instead of failing the rest.
                result = action == "add"
                    ? selection.Add(userId, ToEntries(user, paths))
                    : selection.Remove(userId, paths);

                break;

            default:
                return new BadRequestObjectResult(entryTexts.InvalidRequest);
        }

        return result is null
            ? new ConflictObjectResult(texts.TransferInProgress)
            : Answer(result);
    }

    private JsonResult Answer(SelectionSnapshot snapshot)
        => new(SelectionView.Of(snapshot, status.Of(snapshot.Paths)));

    /// <summary>
    /// The entries that can be selected among the paths: those the user may open, also where a password locks them (the card then offers to
    /// unlock them, and nothing else can be done with them until then). What the card shows about them (may they be deleted and moved) is only what is known
    /// about the entry itself, which takes no time: looking at everything inside a directory would make selecting one wait for that. The
    /// copy, move or delete checks everything inside again, and says what is wrong.
    /// </summary>
    private List<SelectionEntry> ToEntries(ClaimsPrincipal user, string[] virtualPaths)
    {
        // The paths are mostly in the same directory: find them all together (each directory is listed once, not once per path), and read
        // the configuration of the folders above them once for all of them
        using IDisposable scope = ConfigReadScope.Begin();
        using IDisposable ignoringLocks = passwordLocks.IgnoringLocks();
        IReadOnlyList<FileSystemInfo?> found = path.FindEntries(virtualPaths);
        List<SelectionEntry> entries = [];
        for (int i = 0; i < virtualPaths.Length; i++)
        {
            // The root can't be selected
            if (virtualPaths[i].Trim('/') == "" || found[i] is not { } entry || !tree.CanOpen(user, entry))
                continue;

            bool isDirectory = entry is DirectoryInfo;
            entries.Add(new SelectionEntry(path.ToVirtualPath(entry.FullName), isDirectory, tree.GetAccess(user, entry.FullName,
                isDirectory).CanDelete));
        }

        return entries;
    }
}
