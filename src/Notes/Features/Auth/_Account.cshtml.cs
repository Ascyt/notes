using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Auth;

public class _Account : PageModel
{
    public bool IsAuthenticated { get; init; }

    /// <summary>Whether a storage operation that the user may see runs now (the button of their list shows, see _Operations).</summary>
    public bool HasOperations { get; init; }

     /// <summary>Gets the kill switch (and the admin icon).</summary>
    public bool IsAdmin { get; init; }

    public string UserName { get; init; } = "";

    /// <summary>Whether the browser has anything unlocked (see <see cref="Passwords.PasswordUnlocks"/>), which the lock button locks again.</summary>
    public bool HasUnlocked { get; init; }

    /// <summary>
    /// Whether an archived folder that the user may open is unzipped now, which the archive button zips at once (see
    /// <see cref="ZippedFolders.ZipHandler"/>). The lock button goes first: this one is shown only while nothing is unlocked.
    /// </summary>
    public bool HasZippable { get; init; }

    /// <summary>Local URL the login brings the user back to.</summary>
    public string ReturnUrl { get; init; } = "/";
}
