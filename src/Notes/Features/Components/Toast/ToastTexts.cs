namespace Notes.Features.Components.Toast;

/// <summary>
/// The texts of the stack of messages (<see cref="_Toasts"/>) and its script. A site with texts of its own derives from this class and
/// registers it in place of this one, as for <see cref="Toolbar.ToolbarTexts"/>.
/// </summary>
public class ToastTexts
{
    /// <summary>The tooltip of a message.</summary>
    public virtual string ClickToDismiss => "Click to dismiss";
}
