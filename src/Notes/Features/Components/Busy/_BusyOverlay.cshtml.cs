using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Components.Busy;

/// <summary>
/// What covers the page while something long runs (see <c>whileBusy</c> in site.ts). It needs nothing of the page, so every layout has it
/// (through <c>_Overlays</c>).
/// </summary>
public class _BusyOverlay : PageModel
{
    /// <summary>Its styles, by their path in the library (see <see cref="Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Components/Busy/_BusyOverlay.css";
}
