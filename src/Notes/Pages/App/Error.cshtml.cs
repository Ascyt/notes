using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Notes.Extensions;

namespace Notes.Pages.App;

/// <summary>
/// Shown for every failed request: unhandled exceptions (exception handler) and empty error responses such as
/// 404 (status code pages). Both re-execute the request against this page, so it answers whatever method was used.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel(TimeProvider time) : PageModel
{
    public new int StatusCode { get; private set; }

    /// <summary>Path and query the user asked for, not the path of this page.</summary>
    public string RequestedPath { get; private set; } = "";

    public string RequestId { get; private set; } = "";
    public DateTimeOffset Timestamp { get; private set; }

    public bool IsServerError => StatusCode >= 500;
    public bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;
    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Missing and inaccessible paths both answer 404 (so nobody can probe which files exist), which is why a
    /// 404 may just mean the user lacks access, and logging in may help someone who has not.
    /// </summary>
    public bool MayNeedLogin => (IsNotFound || StatusCode == StatusCodes.Status403Forbidden) && !IsAuthenticated;

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

        RequestedPath = HttpContext.OriginalPathAndQuery;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        Timestamp = time.GetUtcNow();
    }
}
