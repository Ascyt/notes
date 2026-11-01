using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages._;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    /// <summary>Ends the local session and the Keycloak one, then returns to the root.</summary>
    public IActionResult OnPost()
        => SignOut(new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
}
