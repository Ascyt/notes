namespace Notes.Features.ZippedFolders;

/// <summary>
/// The points between the steps of zipping and unzipping (<see cref="FolderArchiver"/>) at which the app could stop (a crash, a power cut).
/// The tests stop it at each of them in turn and check that <see cref="FolderArchiver.Recover"/> loses nothing from there.
/// </summary>
public enum ArchiveStep
{
    // ---- Zipping

    /// <summary>A part of the new archive is written (after its first file).</summary>
    WritingArchive,

    /// <summary>The new archive is written and on the disk, not checked yet.</summary>
    ArchiveWritten,

    /// <summary>The new archive was read back and holds exactly what was zipped.</summary>
    ArchiveChecked,

    /// <summary>The new archive took the old one's place; the unzipped copy is still there.</summary>
    ArchiveReplaced,

    /// <summary>The unzipped copy was moved into the work directory to be deleted.</summary>
    UnzippedMovedAway,

    /// <summary>A part of the unzipped copy is deleted.</summary>
    DeletingUnzipped,

    // ---- Unzipping

    /// <summary>A part of the archive is unzipped into the work directory (after its first file).</summary>
    Unzipping,

    /// <summary>All of it is unzipped and on the disk, still in the work directory.</summary>
    Unzipped,

    /// <summary>The unzipped copy is in its place.</summary>
    UnzippedInPlace
}
