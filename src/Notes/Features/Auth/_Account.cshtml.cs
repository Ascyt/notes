using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Auth;

public class _Account : PageModel
{
    public bool IsAuthenticated { get; init; }

     /// <summary>Gets the kill switch (and the admin icon).</summary>
    public bool IsAdmin { get; init; }

    public string UserName { get; init; } = "";

    /// <summary>Whether the browser has anything unlocked (see <see cref="Passwords.PasswordUnlocks"/>), which the lock button locks again.</summary>
    public bool HasUnlocked { get; init; }

    /// <summary>Local URL the login brings the user back to.</summary>
    public string ReturnUrl { get; init; } = "/";
}
