using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Helpers;

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
                // What the card shows about the selection (may it be deleted and moved) is found out here, once.
                result = action == "add"
                    ? selection.Add(userId, paths.Select(ToEntry).OfType<SelectionEntry>().ToList())
                    : selection.Remove(userId, paths);

                break;
                
                SelectionEntry? ToEntry(string virtualPath)
                {
                    string? physical = virtualPath.Trim('/') == "" ? null : path.ToPhysicalPath(config.Dir, virtualPath);
                    if (physical is null || !System.IO.Path.Exists(physical) || !tree.CanOpen(user, physical))
                        return null;

                    TreeAccess inspected = tree.Inspect(user, physical);
                    return new SelectionEntry(path.ToVirtualPath(config.Dir, physical), Directory.Exists(physical), inspected.CanDelete);
                }

            default:
                return new BadRequestObjectResult("Invalid selection action.");
        }

        return result is null
            ? new ConflictObjectResult("A copy or move is in progress.")
            : new JsonResult(result);
    }
}
