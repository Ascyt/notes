using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.Components.Upload;

public class _Upload : PageModel
{
    /// <summary>Size of the pieces large files are uploaded in. Must stay below the server's request body limit (30 MB in Kestrel by default).</summary>
    public int ChunkSize { get; init; } = 8 * 1024 * 1024;
}
