using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Components.Archive;

/// <summary>The dialog that asks how to make an archive before it is downloaded (see <see cref="ArchiveHandler"/>).</summary>
public class _ArchiveModal : PageModel
{
    /// <summary>The script of the dialog, by its path in the library (see <see cref="Assets.ComponentAssets"/>).</summary>
    public const string Script = "Features/Components/Archive/_ArchiveModal.js";

    /// <summary>Whether the toolbar's download button opens it for the directory of the page (the selection card opens it too, where it is).</summary>
    public bool OpensFromToolbar { get; init; }

    /// <summary>
    /// Whether the directory of the page is an archived folder that the user (an admin) may download as it is kept: the dialog then has the
    /// switch for that, on at first, for the directory (not for the selection).
    /// </summary>
    public bool OffersAsIs { get; init; }
}
