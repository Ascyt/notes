namespace Notes.Features.Auth;

/// <summary>
/// The texts of logging in and out: the menu of the account (<see cref="_Account"/>), the notice for someone who isn't logged in
/// (<see cref="_LoginNotice"/>) and the one for a session that ended (<see cref="_SessionNotice"/>). A site with texts of its own derives
/// from this class and registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class AuthTexts
{
    public virtual string LogIn => "Log in";
    public virtual string LogOut => "Log out";
    public virtual string KillSwitch => "Kill switch";

    /// <summary>The notice for someone who is not logged in, with <c>{login}</c> where the link to the login goes (<see cref="LoginLink"/>).</summary>
    public virtual string NotLoggedIn => "Not logged in. Some things can only be seen, and anything can only be changed, after {login}.";

    public virtual string LoginLink => "logging in";

    // Why something can't be done, as the pages tell it before it is tried (AccessTree)

    public virtual string CantDeleteOrMove(string display) => $"Insufficient permissions to delete or move '{display}'.";
    public virtual string CantAddHere => "Insufficient permissions to add entries to this directory.";
    public virtual string CantAddToParent => "Insufficient permissions to add entries to the parent directory.";

    /// <summary>For those who manage the configuration only: a bridge has its place in the settings of the site.</summary>
    public virtual string IsABridge(string virtualPath) => $"'{virtualPath}' is a bridge (Config:Bridges), so it can't be deleted, moved or renamed.";

    /// <summary>For those who manage the configuration only: an exposed directory has its place in the settings of the site.</summary>
    public virtual string HoldsAnExposedDirectory(string virtualPath)
        => $"'{virtualPath}' is or holds a directory the site shows (Config:Exposed), so it can't be deleted, moved or renamed.";

    /// <summary>Why the user was logged out by the app (<see cref="SessionNotice"/>), or null for a notice of no known kind.</summary>
    public virtual string? SessionEnded(string notice) => notice switch
    {
        SessionNotice.Ended => "The session has ended. Logging in again is needed to continue.",
        SessionNotice.Unverified => "The session could not be checked with the login server for too long and has ended. "
            + "Logging in again is needed to continue.",
        _ => null
    };
}
