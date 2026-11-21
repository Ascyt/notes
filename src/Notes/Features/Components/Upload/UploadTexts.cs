namespace Notes.Features.Components.Upload;

/// <summary>
/// The texts of uploading (<see cref="_Upload"/>) and of its script. A site with texts of its own derives from this class and registers it in
/// place of this one, as for <see cref="Toolbar.ToolbarTexts"/>. The script's texts are properties, with what changes in braces.
/// </summary>
public class UploadTexts
{
    public virtual string DropFiles => "Drop files to upload";
    public virtual string Title => "Upload files";
    public virtual string Progress => "Upload progress";
    public virtual string AddMore => "Add more...";
    public virtual string Cancel => "Cancel";
    public virtual string Upload => "Upload";

    // The script's

    /// <summary>The button that takes a file off the list.</summary>
    public virtual string Remove => "Remove";

    public virtual string Uploading => "Uploading {name} ({number} of {count})";
    public virtual string UploadedOne => "Uploaded 1 file.";
    public virtual string UploadedMany => "Uploaded {count} files.";

    /// <summary>Why a file failed, with what was uploaded before it, if anything (<see cref="AlreadyUploadedOne"/>).</summary>
    public virtual string Failed => "Failed to upload {name} ({reason}).{uploaded}";

    public virtual string AlreadyUploadedOne => " 1 file was uploaded.";
    public virtual string AlreadyUploadedMany => " {count} files were uploaded.";

    /// <summary>Why a part of a file failed where the server says nothing about it.</summary>
    public virtual string ServerAnswered => "server responded with {status}";

    public virtual string NetworkError => "network error";

    // The server's

    public virtual string LoginRequired => "Login required to upload files.";
    public virtual string CantCreate(string name) => $"Insufficient permissions to create '{name}' here.";
    public virtual string NoSuchUpload => "No such upload, or the chunk does not continue it.";

    public virtual string TooManyUnfinished(int max)
        => $"There are already {max} unfinished uploads. They have to be finished, or are discarded an hour after their last part arrived.";

    public virtual string PartInProgress => "Another part of this upload is still being written.";
    public virtual string TooLarge(string maxSize) => $"The file is too large to be uploaded (at most {maxSize}).";
    public virtual string DirectoryGone => "The directory was renamed, moved or deleted while the file was uploaded.";

    /// <summary>The name that is free, with a number added, would be too long.</summary>
    public virtual string NumberedNameTooLong(string name, int maxNameBytes)
        => $"'{name}' is taken in this directory, and the free name with a number added would be too long (at most {maxNameBytes} bytes).";
}
