using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Components.Upload;

/// <summary>
/// After the name of an uploaded archive of the listing that is being unzipped (a bar, which <see cref="Script"/> keeps up to date until it
/// ended), or couldn't be (a red mark, whose tooltip says why).
/// </summary>
public class _UnzipMark : PageModel
{
    /// <summary>Asks how the bars of the listing go, by its path in the library (see <see cref="Assets.ComponentAssets"/>).</summary>
    public const string Script = "Features/Components/Upload/_UnzipMark.js";

    /// <summary>How far it got (0 to 1) while it is being unzipped; null if it isn't.</summary>
    public double? Progress { get; init; }

    /// <summary>Why it couldn't be unzipped, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The address of the folder it is in, which is asked how it goes (<see cref="UnzippingUploadHandler"/>).</summary>
    public string DirectoryAddress { get; init; } = "";

    public string Name { get; init; } = "";
}
