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

    /// <summary>An archive that is to be unzipped, where the folder it would go into may not be made.</summary>
    public virtual string CantCreateFolder(string archive, string folder)
        => $"'{archive}' can't be unzipped here: insufficient permissions to create the folder '{folder}'. Untick it to upload it as a file.";
    public virtual string NoSuchUpload => "No such upload, or the chunk does not continue it.";

    public virtual string TooManyUnfinished(int max)
        => $"There are already {max} unfinished uploads. They have to be finished, or are discarded an hour after their last part arrived.";

    public virtual string PartInProgress => "Another part of this upload is still being written.";
    public virtual string TooLarge(string maxSize) => $"The file is too large to be uploaded (at most {maxSize}).";
    /// <summary>A folder the file is to go into is taken by something that isn't one.</summary>
    public virtual string NotAFolder(string name) => $"'{name}' is taken by something that is not a directory.";

    public virtual string DirectoryGone => "The directory was renamed, moved or deleted while the file was uploaded.";

    // Unzipping uploaded archives (the dialog's, the script's, the listing's, the server's)

    /// <summary>The box above the archives of the list, which ticks or unticks the box of each.</summary>
    public virtual string UnzipArchives => "Unzip archives (.zip, .7z) once uploaded";

    public virtual string UnzipArchivesHint
        => "Each ticked one is uploaded as a file, then unzipped into a new folder named after it, which takes its place. Unticked ones stay files. Encrypted archives can't be unzipped here.";

    /// <summary>The box of one archive in the list.</summary>
    public virtual string UnzipOne => "Unzip";

    public virtual string UnzipOneLabel => "Unzip {name} once uploaded";

    /// <summary>The bar after an archive of the listing while it is unzipped.</summary>
    public virtual string UnzippingNow => "Being unzipped";

    public virtual string UnzippedInto => "{name} was unzipped into {folder}.";

    /// <summary>The mark after an archive of the listing that couldn't be unzipped (with the reason after it).</summary>
    public virtual string CouldNotUnzip => "Couldn't be unzipped: {reason}";

    /// <summary>After unzipping, where what can't be an entry was left out (configurations, archives of archived folders, data of the app).</summary>
    public virtual string LeftOutOfArchive => "{count} entries of {name} were left out: configurations, archives of archived folders and the app's data can't be uploaded.";

    public virtual string NotAnArchive => "Only a .zip or a .7z can be unzipped.";
    public virtual string ArchiveDamaged(string name) => $"'{name}' is damaged, or not what its name says.";
    public virtual string ArchiveEncrypted(string name) => $"'{name}' is encrypted. Unzip it yourself, then upload what is in it.";

    public virtual string ArchiveDamagedOrEncrypted(string name)
        => $"'{name}' is damaged, or encrypted (then unzip it yourself, and upload what is in it).";

    public virtual string UnzipFailed(string name) => $"'{name}' couldn't be unzipped on the server; it is logged.";
    public virtual string BadEntryName(string entry, string problem) => $"'{entry}' in the archive can't be a name here: {problem}";

    public virtual string UnzippedTooLarge(string maxSize)
        => $"Unzipped, it would be larger than may be uploaded (at most {maxSize}).";

    public virtual string TooManyEntries(int max) => $"It has more entries than may be unzipped at once (at most {max}).";

    // Files that are there already, cancelling, and going on after the page was closed (the script's)

    /// <summary>Before a file is uploaded, while it is compared with the file of its name that is there.</summary>
    public virtual string Comparing => "Comparing {name} with the one that is there ({number} of {count})";

    /// <summary>The tag of a file of the list that is there already, the same.</summary>
    public virtual string AlreadyThere => "already there";

    public virtual string Done => "uploaded";

    /// <summary>When going on with an upload: a file that was in it, but wasn't picked again.</summary>
    public virtual string PickAgain => "pick it again";

    public virtual string UploadedWithSkipped => "Uploaded {count} files ({skipped} of them were there already).";
    public virtual string NothingNew => "All {count} files were there already.";
    public virtual string Cancelled => "Upload cancelled.{uploaded}";

    /// <summary>The page that opens after an upload that didn't finish (another tab, or one that was closed).</summary>
    public virtual string ResumeTitle => "Unfinished upload";

    public virtual string ResumeText => "An upload into {directory} didn't finish: {done} of {count} files are there. Continue it?";
    public virtual string ResumeContinue => "Continue";
    public virtual string ResumeDiscard => "Discard";

    public virtual string ResumePick
        => "Pick the same files again (browsers can't keep them): those that are there already are skipped, a half-sent one goes on where it stopped.";

    public virtual string PickFiles => "Pick files...";

    /// <summary>The name that is free, with a number added, would be too long.</summary>
    public virtual string NumberedNameTooLong(string name, int maxNameBytes)
        => $"'{name}' is taken in this directory, and the free name with a number added would be too long (at most {maxNameBytes} bytes).";
}
