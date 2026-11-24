using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.ZippedFolders;

/// <summary>The mark after the name of an archived folder, which says whether it is zipped, unzipped or being zipped now.</summary>
public class _ArchivedMark : PageModel
{
    public ArchiveState State { get; init; }

    /// <summary>How far its zipping got (0 to 1), while it is being zipped.</summary>
    public double Progress { get; init; }

    /// <summary>
    /// The folder's address, where the page asks how far its zipping got (<see cref="ZippingHandler"/>): only in the listing. Without it, one
    /// being zipped is marked as unzipped (the breadcrumb: a page inside it stops the zipping anyway).
    /// </summary>
    public string? Address { get; init; }
}
