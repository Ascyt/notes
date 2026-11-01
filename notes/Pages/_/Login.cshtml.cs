using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages._;

public sealed class LoginModel : PageModel
{
    /// <summary>Sends the user to Keycloak, then back to <paramref name="returnUrl"/> (a local URL, otherwise the root).</summary>
    public IActionResult OnGet(string? returnUrl = null)
    {
        string target = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(target);

        return Challenge(new AuthenticationProperties { RedirectUri = target }, OpenIdConnectDefaults.AuthenticationScheme);
    }
}
