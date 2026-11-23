namespace Notes.Features.Passwords;

/// <summary>
/// The texts of the passwords of entries: the dialog that asks for one (<see cref="_PasswordModal"/>), the page of a locked entry, the button
/// that locks everything again, and what the server answers. A site with texts of its own derives from this class and registers it in place
/// of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>. The script's texts are properties, with what changes in braces.
/// </summary>
public class PasswordTexts
{
    // The dialog
    public virtual string Title => "Password required";

    /// <summary>What the dialog and the page of a locked entry say, with the entry in braces ({entry}).</summary>
    public virtual string IsProtected => "'{entry}' is password-protected.";

    public virtual string Password => "Password";
    public virtual string ShowPassword => "Show password";
    public virtual string Close => "Close";
    public virtual string Cancel => "Cancel";
    public virtual string Unlock => "Unlock";

    /// <summary>How long until the next password can be tried, counting down ({seconds}).</summary>
    public virtual string Wait => "Too many incorrect passwords. Next try in {seconds} s.";

    public virtual string ConnectionLost => "Connection lost while checking the password.";
    public virtual string Failed => "Password check failed ({status}).";

    /// <summary>On the page of a locked entry, the button that opens the dialog.</summary>
    public virtual string EnterPassword => "Enter password";

    /// <summary>The open lock after an entry whose password is unlocked (in listings and the breadcrumb).</summary>
    public virtual string UnlockedMark => "Password-protected, unlocked";

    // The button that locks everything again
    public virtual string LockAll => "Lock all unlocked entries (passwords needed again)";
    public virtual string LockedAll => "All entries locked.";

    /// <summary>When keeping the entry unlocked while it is edited finds that it locked meanwhile.</summary>
    public virtual string Expired => "Unlock expired. Saving asks for the password again; unsaved changes are kept.";

    // The server's
    public virtual string Locked(string display) => $"'{display}' is password-protected.";
    public virtual string Wrong => "Incorrect password.";

    public virtual string WaitSeconds(int seconds)
        => $"Too many incorrect passwords. Next try in {seconds} s.";

    public virtual string NotAPassword(PasswordProblem problem) => problem switch
    {
        PasswordProblem.Empty => "Empty password.",
        PasswordProblem.TooLong => $"Password longer than {PasswordHashing.MaxBytes} bytes (UTF-8), so it can't be correct.",
        _ => "Password contains control characters."
    };
}
