using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Passwords;

/// <summary>
/// The page of an entry that a password locks (it, or a directory above it): it names the entry that has the password and asks for it with
/// the dialog (<see cref="_PasswordModal"/>), which opens by itself. Once it is unlocked, the page is loaded again.
/// </summary>
public class _PasswordPrompt : PageModel
{
    /// <summary>The address of the entry that has the password.</summary>
    public string Address { get; init; } = "";

    /// <summary>Its virtual path, as the user sees it.</summary>
    public string Display { get; init; } = "";

    /// <summary>Whether it is a file (for the breadcrumb's icon).</summary>
    public bool IsFile { get; init; }
}
