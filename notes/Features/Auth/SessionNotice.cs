using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Extensions;

namespace notes.Features.Auth;

/// <summary>
/// The reason a user was logged out by the app (and not by themselves), so that the next page can say why they have to log in again. It
/// lives in <see cref="HttpContext.Items"/> for the rest of the request that ended the session, and in a short-lived cookie for the one after
/// (a script's request, whose answer nobody reads as a page, ends a session as well).
/// </summary>
public static class SessionNotice
{
    /// <summary>Keycloak ended the session: the user logged out elsewhere, was disabled, or was away for too long.</summary>
    public const string Ended = "ended";

    /// <summary>Keycloak couldn't be asked about the session for too long.</summary>
    public const string Unverified = "unverified";

    private const string CookieName = ".notes.notice";
    private const string ItemKey = "notes.session-notice";

    public static void Set(HttpContext context, string notice)
    {
        context.Items[ItemKey] = notice;
        // A page that is being made shows the notice itself; only the answer to a script has to leave it for the next page
        if (!HttpMethods.IsGet(context.Request.Method))
            context.SetNoticeCookie(CookieName, notice, TimeSpan.FromMinutes(5));
    }

    /// <summary>Picks up the notice a previous request left in the cookie, so that this request shows it once.</summary>
    public static IApplicationBuilder UseSessionNotice(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) && context.TakeNoticeCookie(CookieName) is { } notice)
                context.Items[ItemKey] = notice;

            await next(context);
        });

    /// <summary>What to tell the user on this page, or null if there is nothing to tell.</summary>
    public static string? MessageFor(HttpContext context)
        => context.Items[ItemKey] switch
        {
            Ended => "The session has ended. Logging in again is needed to continue.",
            Unverified => "The session could not be checked with the login server for too long and has ended. Logging in again is needed to continue.",
            _ => null
        };

    /// <summary>Which notice this page has (<see cref="Ended"/>, <see cref="Unverified"/>), or null.</summary>
    public static string? Of(HttpContext context) => context.Items[ItemKey] as string;
}

/// <summary>The notice on a page of the notes for someone who is not logged in: that they may need to, to see more or to change anything.</summary>
public class _LoginNotice : PageModel
{
    /// <summary>Where the login leads back to.</summary>
    public string ReturnUrl { get; init; } = "/";
}

/// <summary>The notice at the top of a page that tells the user why they were logged out.</summary>
public class _SessionNotice : PageModel
{
    public string Message { get; init; } = "";

    /// <summary>Where the login leads back to.</summary>
    public string ReturnUrl { get; init; } = "/";
}
