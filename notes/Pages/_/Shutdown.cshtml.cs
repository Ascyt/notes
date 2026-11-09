using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Auth;
using notes.Features.KillSwitch;

namespace notes.Pages._;

/// <summary>
/// Where the kill switch in the navigation bar (admins only) posts to. Named vaguely, as is everything the browser gets: why the site went
/// down is only in the log.
/// </summary>
[Authorize(Policy = NotesAccess.AdminPolicyName)]
public sealed class ShutdownModel(KillSwitchService killSwitch) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    /// <summary>Shuts the site down, then sends the admin to the shutdown page that everyone gets from now on.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await killSwitch.EngageAsync(User.Identity?.Name ?? "an unknown admin");
        return LocalRedirect("/");
    }
}
