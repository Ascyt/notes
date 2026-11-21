namespace Notes.Features.Media;

/// <summary>
/// The messages about the bytes of media files (<c>?raw</c>). A site with texts of its own derives from this class and registers it in place
/// of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class MediaTexts
{
    /// <summary>Why a file that isn't media has no raw bytes to show.</summary>
    public virtual string NotMedia => "Only media files (images, video, audio and PDF) can be viewed raw. The file can be downloaded instead.";
}
