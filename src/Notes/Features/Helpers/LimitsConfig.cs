using System.Text;

namespace Notes.Features.Helpers;

/// <summary>
/// How large what users send and make may be (<c>Config:Limits</c>, see docs/development/user-input.md). The defaults are far beyond what normal
/// use comes near; they are there so that nothing is unbounded, not to restrict anyone.
/// </summary>
public sealed class LimitsConfig
{
    /// <summary>The largest file that may be uploaded.</summary>
    public long MaxUploadBytes { get; init; } = 64L * 1024 * 1024 * 1024;

    /// <summary>
    /// How many unfinished uploads one user may have at a time (each is a temporary file until it is finished or cleared as stale).
    /// </summary>
    public int MaxUploadsInProgressPerUser { get; init; } = 100;

    /// <summary>
    /// The largest text file that is shown in the editor or rendered, and the largest text that may be saved. A larger file is shown as one that
    /// can't be viewed (it can still be downloaded). Must stay below the server's request body limit (30 MB in Kestrel by default).
    /// </summary>
    public int MaxTextBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>The longest virtual path (UTF-8 bytes, without the leading <c>/</c>) a new entry may get, so that its address stays usable.</summary>
    public int MaxPathBytes { get; init; } = 2048;

    /// <summary>
    /// The largest image file that is converted for the browser (<c>?raw</c> of HEIC, raw photos, …). Larger ones can still be downloaded.
    /// </summary>
    public int MaxConvertedImageBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>
    /// The most pixels an image that is converted for the browser may have, so that a small file claiming a huge image can't take up all memory.
    /// </summary>
    public ulong MaxConvertedImagePixels { get; init; } = 200_000_000;

    /// <summary>Whether a new entry at the virtual path would break <see cref="MaxPathBytes"/>.</summary>
    public bool IsTooLong(string virtualPath)
        => Encoding.UTF8.GetByteCount(virtualPath) > MaxPathBytes;


    /// <summary>Stops the app from starting with a limit that makes no sense (zero, negative, or above what the server can take).</summary>
    public void Check()
    {
        if (MaxUploadBytes <= 0 || MaxUploadsInProgressPerUser <= 0 || MaxTextBytes <= 0 || MaxPathBytes <= 0 || MaxConvertedImageBytes <= 0
            || MaxConvertedImagePixels == 0)
            throw new InvalidOperationException("Every value of Config:Limits must be greater than zero.");
        if (MaxPathBytes < NewNames.MaxNameBytes)
            throw new InvalidOperationException($"Config:Limits:MaxPathBytes must be at least {NewNames.MaxNameBytes}, the longest name.");
    }
}
