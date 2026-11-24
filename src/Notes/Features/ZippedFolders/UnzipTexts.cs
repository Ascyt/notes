namespace Notes.Features.ZippedFolders;

/// <summary>
/// The texts of unzipping an archived folder: the page that waits for it (<see cref="_Unzipping"/>), the script that waits for it where a
/// request of a page needs it, what the server answers (<see cref="ArchiveGate"/>), and the archive button. A site with texts of its own
/// derives from this class and registers it in place of this one. The script's texts are properties, with what changes in braces.
/// </summary>
public class UnzipTexts
{
    public virtual string Unzipping => "Unzipping '{entry}'…";

    /// <summary>The icon after the name of an archived folder that is zipped now (in the listing and the breadcrumb).</summary>
    public virtual string Zipped => "Kept as an archive: zipped now, unzipped when it is opened";

    /// <summary>The icon after the name of an archived folder that is unzipped now.</summary>
    public virtual string Unzipped => "Kept as an archive: unzipped now, zipped again once it isn't used for a while";

    /// <summary>The bar after the name of an archived folder that is being zipped now (in the listing).</summary>
    public virtual string ZippingNow => "Being zipped: opening it stops that";

    /// <summary>The archive button of the navbar (<see cref="ZipHandler"/>).</summary>
    public virtual string ZipAll => "Zip the archived folders that are unzipped now";


    public virtual string Failed => "Unzipping failed. Try again; if it keeps failing, the archive may be damaged.";
    public virtual string ConnectionLost => "Connection lost while unzipping.";

    /// <summary>Where the server says nothing about why.</summary>
    public virtual string FailedWithStatus => "Unzipping failed ({status}).";
}
