using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using notes.Auth;
using notes.Pages.Extensions;

namespace notes.Pages._;

/// <summary>
/// Shown for every failed request: unhandled exceptions (exception handler) and empty error responses such as
/// 404 (status code pages). Both re-execute the request against this page, so it answers whatever method was used.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public int StatusCode { get; private set; }
    public string Reason { get; private set; } = "";
    public string Description { get; private set; } = "";

    /// <summary>Path and query the user asked for, not the path of this page.</summary>
    public string RequestedPath { get; private set; } = "";

    public string RequestId { get; private set; } = "";
    public DateTimeOffset Timestamp { get; private set; }

    public bool IsServerError => StatusCode >= 500;
    public bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;
    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Missing and inaccessible paths both answer 404 (so nobody can probe which files exist), which is why a
    /// 404 may just mean the user lacks access.
    /// </summary>
    public bool MayNeedLogin => IsNotFound && !IsAdmin;
    public bool IsAdmin => User.IsInRole(NotesAccess.AdminRole);

    public void OnGet() => Init();
    public void OnPost() => Init();
    public void OnPut() => Init();
    public void OnPatch() => Init();
    public void OnDelete() => Init();

    private void Init()
    {
        // Visiting this page directly isn't an error response, so it reads as an unknown page
        StatusCode = Response.StatusCode >= 400 ? Response.StatusCode : StatusCodes.Status404NotFound;
        Response.StatusCode = StatusCode;

        Reason = ReasonPhrases.GetReasonPhrase(StatusCode) is { Length: > 0 } reason ? reason : "Error";
        Description = StatusCode switch
        {
            StatusCodes.Status400BadRequest => "The request could not be understood by the server.",
            StatusCodes.Status401Unauthorized => "You need to log in to do that.",
            StatusCodes.Status403Forbidden => "You are not allowed to do that.",
            StatusCodes.Status404NotFound => "There is nothing at this address.",
            StatusCodes.Status405MethodNotAllowed => "This address does not support that kind of request.",
            StatusCodes.Status408RequestTimeout => "The request took too long to arrive.",
            StatusCodes.Status413PayloadTooLarge => "The request was too large.",
            StatusCodes.Status429TooManyRequests => "Too many requests were made in a short time. Try again in a moment.",
            StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable or StatusCodes.Status504GatewayTimeout
                => "The server is temporarily unavailable. Try again in a moment.",
            >= 500 => "Something went wrong on the server while handling your request.",
            _ => "The request could not be completed."
        };

        RequestedPath = HttpContext.OriginalPathAndQuery;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        Timestamp = DateTimeOffset.UtcNow;
    }
}
