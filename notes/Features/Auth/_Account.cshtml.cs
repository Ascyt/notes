using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Auth;

public class _Account : PageModel
{
    public bool IsAuthenticated { get; init; }

    public string UserName { get; init; } = "";

    /// <summary>Local URL the login brings the user back to.</summary>
    public string ReturnUrl { get; init; } = "/";
}
