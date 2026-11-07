using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Helpers;
using notes.Features.Permissions;

namespace notes.Features.Components.Selection;

/// <summary>Body of the requests that select or unselect entries.</summary>
public sealed record SelectionRequest(string[]? Paths);

/// <summary>
/// Answers <c>?selection</c>. A GET returns the user's selection; a POST changes it: <c>?selection=add</c> and <c>?selection=remove</c>
/// take the virtual paths in the body, <c>?selection=clear</c> unselects everything. Every answer is the selection as it is afterwards.
/// </summary>
public sealed class SelectionHandler(Config config, IPathService path, AccessTree tree, ISelectionService selection)
{
    /// <summary>More entries than this in one request is not something a page does.</summary>
    private const int MaxPathsPerRequest = 10_000;

    /// <summary>The user's id (Keycloak's "sub" claim), or null if there is none. The selection is stored under it.</summary>
    public static string? UserIdOf(ClaimsPrincipal user)
        => user.FindFirst("sub")?.Value;

    public IActionResult HandleGet(ClaimsPrincipal user)
    {
        string? userId = UserIdOf(user);
        if (userId is null || !tree.CanOpen(user, config.Dir))
            return new NotFoundResult();

        return new JsonResult(selection.Get(userId));
    }

    public async Task<IActionResult> HandlePostAsync(HttpRequest request, ClaimsPrincipal user, string action)
    {
        string? userId = UserIdOf(user);
        if (userId is null || !tree.CanOpen(user, config.Dir))
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
                    return new BadRequestObjectResult("Invalid request.");

                SelectionRequest? body;
                try
                {
                    body = await request.ReadFromJsonAsync<SelectionRequest>();
                }
                catch (JsonException)
                {
                    return new BadRequestObjectResult("Invalid request.");
                }

                if (body?.Paths is not { } paths || paths.Length > MaxPathsPerRequest)
                    return new BadRequestObjectResult("Invalid request.");

                // Paths are stored the way the listing shows them. Unselecting needs no checks (the entry may be gone by now),
                // and entries that can't be selected (gone, hidden, may not be opened) are left out instead of failing the rest.
                result = action == "add"
                    ? selection.Add(userId, ToEntries(user, paths))
                    : selection.Remove(userId, paths);

                break;

            default:
                return new BadRequestObjectResult("Invalid selection action.");
        }

        return result is null
            ? new ConflictObjectResult("A copy or move is in progress.")
            : new JsonResult(result);
    }

    /// <summary>
    /// The entries that can be selected among the paths. What the card shows about them (may they be deleted and moved) is only what is known
    /// about the entry itself, which takes no time: looking at everything inside a directory would make selecting one wait for that. The
    /// copy, move or delete checks everything inside again, and says what is wrong.
    /// </summary>
    private List<SelectionEntry> ToEntries(ClaimsPrincipal user, string[] virtualPaths)
    {
        // The paths are mostly in the same directory: find them all together (each directory is listed once, not once per path), and read
        // the configuration of the folders above them once for all of them
        using IDisposable scope = ConfigReadScope.Begin();
        IReadOnlyList<FileSystemInfo?> found = path.FindEntries(config.Dir, virtualPaths);
        List<SelectionEntry> entries = [];
        for (int i = 0; i < virtualPaths.Length; i++)
        {
            // The root can't be selected
            if (virtualPaths[i].Trim('/') == "" || found[i] is not { } entry || !tree.CanOpen(user, entry))
                continue;

            bool isDirectory = entry is DirectoryInfo;
            entries.Add(new SelectionEntry(path.ToVirtualPath(config.Dir, entry.FullName), isDirectory, tree.GetAccess(user, entry.FullName, isDirectory).CanDelete));
        }

        return entries;
    }
}
