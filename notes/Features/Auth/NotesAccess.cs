using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace notes.Features.Auth;

public static class NotesAccess
{
    /// <summary>The policy of what only admins may reach as a whole (the Hangfire dashboard). What is done with the notes is decided per entry, see <see cref="IEntryAccess"/>.</summary>
    public const string AdminPolicyName = "Admin";

    public const string AdminRole = "admin";
}

/// <summary>The claims of a session that the permissions are made of. Keycloak sends them under these names (see keycloak/notes-realm.json).</summary>
public static class NotesClaims
{
    /// <summary>The name the user logs in with.</summary>
    public const string UserName = "preferred_username";

    /// <summary>The id Keycloak gives the user, which never changes.</summary>
    public const string UserId = "sub";

    /// <summary>The names of the groups the user is in.</summary>
    public const string Groups = "groups";

    /// <summary>The realm roles the user has (also those they have through their groups).</summary>
    public const string Roles = "roles";
}

/// <summary>
/// Answers every failed authorization with the same 404 the page gives for a missing path, so
/// nobody without access can tell which files and directories exist. Applies to all [Authorize] endpoints.
/// </summary>
public sealed class NotFoundAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        // No body: the error page (status code pages) fills it in, identically to a missing path
        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }
}
