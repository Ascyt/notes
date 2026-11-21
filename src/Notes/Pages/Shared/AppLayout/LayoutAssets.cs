namespace Notes.Pages.Shared.AppLayout;

/// <summary>
/// The styles and scripts every layout of the app has (through <c>_Head</c> and <c>_Scripts</c>), by their paths in the library (see
/// <see cref="Features.Components.Assets.ComponentAssets"/>, which can replace each of them).
/// </summary>
public static class LayoutAssets
{
    /// <summary>What all pages share: the look of a button that deletes, the header that stays at the top, and the like.</summary>
    public const string Styles = "Pages/Shared/AppLayout/site.css";

    /// <summary>What the scripts of all pages use: the disabled and busy look of buttons, the addresses of the app, their texts.</summary>
    public const string Script = "Pages/Shared/AppLayout/site.js";

    /// <summary>Light and dark, set before the page is painted.</summary>
    public const string Theme = "Pages/Shared/AppLayout/theme.js";

    /// <summary>The keyboard shortcuts of every page (the <c>key</c> attributes).</summary>
    public const string ShortcutStyles = "Features/Shortcuts/shortcuts.css";
    public const string Shortcuts = "Features/Shortcuts/shortcuts.js";
}
