using Microsoft.AspNetCore.Mvc.RazorPages;
using Notes.Features.Components.Upload;

namespace Notes.Features.Editor.InsertMedia;

public class _InsertMedia : PageModel
{
    /// <summary>The styles and the script of the dialog, by their paths in the library (see <see cref="Components.Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Editor/InsertMedia/_InsertMedia.css";
    public const string Script = "Features/Editor/InsertMedia/_InsertMedia.js";

    /// <summary>The markdown file the link is written into, whose directory the path of the file is relative to.</summary>
    public string FileVirtualPath { get; init; } = "";

    /// <summary>Where a file goes unless another path is given, relative to the markdown file; its name is added.</summary>
    public string DefaultFolder { get; init; } = "./assets/";

    /// <summary>Size of the pieces the file is uploaded in, as for any upload.</summary>
    public int ChunkSize { get; init; } = new _Upload().ChunkSize;
}
