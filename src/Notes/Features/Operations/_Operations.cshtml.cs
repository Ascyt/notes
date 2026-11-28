using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Operations;

/// <summary>
/// The dialog of the storage operations that run now (opened by the button in the navbar, which its script shows while there are any), on
/// every page of who is logged in.
/// </summary>
public class _Operations : PageModel
{
    public const string Script = "Features/Operations/_Operations.js";
}
