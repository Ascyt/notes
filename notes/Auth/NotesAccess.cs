using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace notes.Auth;

public static class NotesAccess
{
    public const string PolicyName = "NotesAccess";
    public const string AdminRole = "admin";
}

/// <summary>Admins may do anything; everyone else, signed in or not, may only view the root page (it carries the login button).</summary>
public sealed class NotesAccessRequirement : IAuthorizationRequirement;

public sealed class NotesAccessHandler : AuthorizationHandler<NotesAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, NotesAccessRequirement requirement)
    {
        if (context.User.IsInRole(NotesAccess.AdminRole) || IsRootView(context.Resource as HttpContext))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    private static bool IsRootView(HttpContext? http)
        => http is not null
           && http.Request.Path == "/"
           && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));
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

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Path not found.");
    }
}
