namespace Notes.Features.ZippedFolders;

/// <summary>How an archived folder is kept right now (see <see cref="ArchivedFolders.ArchiveStateOf"/>).</summary>
public enum ArchiveState
{
    /// <summary>Only its archive is there: going into it unzips it first.</summary>
    Zipped,

    /// <summary>Its unzipped copy is there (and counts), until it is zipped again.</summary>
    Unzipped,

    /// <summary>
    /// It is being zipped (or zipping waits for what uses it): opening it stops that where it may, as the copy is still there until the new
    /// archive took the old one's place.
    /// </summary>
    Zipping
}
