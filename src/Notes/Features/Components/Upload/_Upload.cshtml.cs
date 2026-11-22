using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.Components.Upload;

public class _Upload : PageModel
{
    /// <summary>The styles and the script of uploading, by their paths in the library (see <see cref="Notes.Features.Components.Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/Components/Upload/_Upload.css";
    public const string Script = "Features/Components/Upload/_Upload.js";

    /// <summary>Sending a file in chunks, which uploading shares with the media of the editor (InsertMedia).</summary>
    public const string ChunksScript = "Features/Components/Upload/upload-chunks.js";

    /// <summary>
    /// Size of the pieces large files are uploaded in. Must stay below the server's request body limit (30 MB in Kestrel by default).
    /// </summary>
    public int ChunkSize { get; init; } = 8 * 1024 * 1024;

    /// <summary>Why nothing can be uploaded here, or null if it can. Files dropped on the page then only get this as a message.</summary>
    public string? DisabledReason { get; init; }
}
