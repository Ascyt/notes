namespace Notes.Features.Components.Busy;

/// <summary>
/// The texts of what covers the page while something long runs (<see cref="_BusyOverlay"/>). A site with texts of its own derives from this
/// class and registers it in place of this one, as for <see cref="Toolbar.ToolbarTexts"/>. What is running says itself what it does.
/// </summary>
public class BusyTexts
{
    /// <summary>The spinner, read out.</summary>
    public virtual string Working => "Working";
}
