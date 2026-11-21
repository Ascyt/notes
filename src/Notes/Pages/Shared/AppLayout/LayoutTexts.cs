namespace Notes.Pages.Shared.AppLayout;

/// <summary>
/// The texts of the layout of the app and of its parts that a site's layout may take as well (the menu, the theme button). A site with
/// texts of its own derives from this class and registers it in place of this one, as for
/// <see cref="Features.Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class LayoutTexts
{
    public virtual string Menu => "Menu";
    public virtual string About => "About";
    public virtual string Hangfire => "Hangfire";

    /// <summary>The button says what it does, not what is shown.</summary>
    public virtual string SwitchToDark => "Switch to dark mode";
    public virtual string SwitchToLight => "Switch to light mode";

    public virtual string ShortcutsHint => "Holding Right Ctrl shows the keyboard shortcuts.";

    /// <summary>In place of <see cref="ShortcutsHint"/> while the shortcuts are shown.</summary>
    public virtual string ShortcutsShownHint => "Hovering over a shortcut shows it in full.";

    public virtual string LicensedUnder => "Licensed under";
    public virtual string SourceCode => "Source code";
}
