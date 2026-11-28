using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.ZippedFolders;

/// <summary>The mark after the name of an archived folder, which says whether it is zipped, unzipped or being zipped now.</summary>
public class _ArchivedMark : PageModel
{
    public ArchiveState State { get; init; }

    /// <summary>Whether it is archived encrypted (as the configurations say now): a shield in place of the zipper.</summary>
    public bool Encrypted { get; init; }

    /// <summary>How far its zipping got (0 to 1), while it is being zipped.</summary>
    public double Progress { get; init; }

    /// <summary>
    /// The folder's address, where the page asks how far its zipping got (<see cref="ZippingHandler"/>): only in the listing. Without it, one
    /// being zipped is marked as unzipped (the breadcrumb: a page inside it stops the zipping anyway).
    /// </summary>
    public string? Address { get; init; }

    /// <summary>The icon's classes for the zipped mark (solid) or the unzipped one; _Unzipping.ts has the same.</summary>
    public string IconClasses(bool zipped) => (Encrypted, zipped) switch
    {
        (true, true) => "fa-solid fa-shield fa-xs ms-1 text-warning",
        (true, false) => "fa-solid fa-shield-halved fa-xs ms-1 text-secondary",
        (false, true) => "fa-solid fa-file-zipper fa-xs ms-1 text-primary",
        (false, false) => "fa-regular fa-file-zipper fa-xs ms-1 text-secondary"
    };

    public string Title(UnzipTexts texts, bool zipped) => (Encrypted, zipped) switch
    {
        (true, true) => texts.EncryptedZipped,
        (true, false) => texts.EncryptedUnzipped,
        (false, true) => texts.Zipped,
        (false, false) => texts.Unzipped
    };
}
