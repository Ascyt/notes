namespace Notes.Features.KillSwitch;

/// <summary>
/// The texts of the confirmation of the kill switch (<see cref="_KillSwitchModal"/>). A site with texts of its own derives from this class
/// and registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class KillSwitchTexts
{
    public virtual string Title => "Shut down the site?";
    public virtual string WhatItDoes => "This takes the site down and disables the login server's realm, for everyone. All sessions end at once.";
    public virtual string HowItComesBack => "The site stays down until it is brought back by hand on the server.";
    public virtual string Close => "Close";
    public virtual string Cancel => "Cancel";
    public virtual string ShutDown => "Shut down";

    // The page everyone gets once the app is shut down, vague on purpose: why is nobody's business but the admins'
    public virtual string ShutDownTitle => "Shut down";
    public virtual string ShutDownMessage => "This service has been shut down. Please check back later.";
}
