namespace Notes.Features.Operations;

/// <summary>
/// The texts of the list of operations (<see cref="_Operations"/>) and of what it answers. A site with texts of its own derives from this class
/// and registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class OperationTexts
{
    /// <summary>The button in the navbar, and the title of the dialog.</summary>
    public virtual string Title => "Running storage operations";

    public virtual string Close => "Close";
    public virtual string Cancel => "Cancel";
    public virtual string Nothing => "Nothing is running now.";

    /// <summary>Who started what the app does by itself (zipping idle folders, going on after a restart).</summary>
    public virtual string TheApp => "the app";

    public virtual string StartedBy => "by {user}";
    public virtual string Cancelling => "Cancelling…";
    public virtual string MayNotCancel => "Only who started it, or an admin, may cancel it.";
    public virtual string CancelFailed => "It could not be cancelled ({status}).";

    // The server's

    public virtual string NotFound => "That operation is no longer running.";

    /// <summary>What an operation is called in the list (where it runs is next to it).</summary>
    public virtual string Name(OperationKind kind) => kind switch
    {
        OperationKind.Zipping => "Zipping an archived directory",
        OperationKind.Unzipping => "Unzipping an archived directory",
        OperationKind.PreparingDownload => "Preparing a download",
        OperationKind.UnzippingUpload => "Unzipping an uploaded archive",
        OperationKind.Copying => "Copying selected entries here",
        OperationKind.Moving => "Moving selected entries here",
        _ => "Deleting selected entries"
    };

    /// <summary>What an operation is called where something waits for it: what it does, and where.</summary>
    public virtual string TitleOf(OperationKind kind, string where) => kind switch
    {
        OperationKind.Zipping => $"Zipping {where}",
        OperationKind.Unzipping => $"Unzipping {where}",
        OperationKind.PreparingDownload => $"Preparing a download of {where}",
        OperationKind.UnzippingUpload => $"Unzipping the upload {where}",
        OperationKind.Copying => $"Copying into {where}",
        OperationKind.Moving => $"Moving into {where}",
        _ => $"Deleting in {where}"
    };
}
