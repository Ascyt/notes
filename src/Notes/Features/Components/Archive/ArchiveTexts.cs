namespace Notes.Features.Components.Archive;

/// <summary>
/// The texts of downloading as an archive: the dialog that asks how (<see cref="_ArchiveModal"/>), its script, and what the server answers
/// (<see cref="ArchiveHandler"/>). A site with texts of its own derives from this class and registers it in place of this one, as for
/// <see cref="Toolbar.ToolbarTexts"/>. The script's texts are properties, with what changes in braces.
/// </summary>
public class ArchiveTexts
{
    // The dialog
    public virtual string Title => "Download as an archive";
    public virtual string Format => "Format";
    public virtual string Compression => "Compression";
    public virtual string Store => "None (store)";

    /// <summary>The lowest level of a 7z, which can't be stored without compression.</summary>
    public virtual string Min => "Min";
    public virtual string Medium => "Medium";
    public virtual string High => "High";
    public virtual string Max => "Max";
    public virtual string Password => "Password";
    public virtual string PasswordHint => "Optional. Without one, the archive is not encrypted.";
    public virtual string ShowPassword => "Show password";
    public virtual string Close => "Close";
    public virtual string Cancel => "Cancel";
    public virtual string Download => "Download";

    // The script's
    public virtual string Preparing => "Preparing the archive…";
    public virtual string LeftOutUnreadable => "Entries without read access were left out of the archive.";
    public virtual string ConnectionLost => "The connection was lost while the archive was being prepared.";

    /// <summary>Where the server says nothing about why.</summary>
    public virtual string Failed => "Failed to prepare the archive ({status}).";

    // The server's
    public virtual string LoginRequired => "Login required to download as an archive.";
    public virtual string NotADirectory => "Only a directory or the selection can be downloaded as an archive.";
    public virtual string AlreadyPreparing => "An archive is already being prepared (in another tab?).";
    public virtual string SelectionChanged => "The selection was changed in the meantime (in another tab?). Nothing was downloaded.";
    public virtual string SelectionBusy => "A copy, move or delete of the selection is in progress.";
    public virtual string NothingSelected => "Nothing is selected.";
    public virtual string SelectedEntryGone => "A selected entry no longer exists.";
    public virtual string NoReadAccess(string display) => $"No read access to '{display}', so it can't be downloaded.";

    public virtual string PermissionsChanged
        => "The permissions of an entry in the archive changed while it was being prepared. Nothing was downloaded; trying again may help.";

    public virtual string ServerCantRead => "Part of what was to go into the archive can't be read by the server.";
    public virtual string ArchiveGone => "The archive is no longer there. Downloading again prepares a new one.";
}
