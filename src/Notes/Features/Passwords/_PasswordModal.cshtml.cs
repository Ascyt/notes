using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Passwords;

/// <summary>
/// The dialog that asks for the password of a locked entry, on every page (it is part of the layout's overlays): whenever the server answers
/// a request of the page's scripts with <see cref="PasswordGate.RequiredHeader"/>, it asks, unlocks the entry and sends the request again, so
/// nothing that was being done (an edit that wasn't saved yet) is lost.
/// </summary>
public class _PasswordModal : PageModel
{
    /// <summary>The styles and the script of the dialog, by their paths in the library (see <see cref="Components.Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Passwords/_PasswordModal.css";
    public const string Script = "Features/Passwords/_PasswordModal.js";
}
