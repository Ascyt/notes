using Microsoft.AspNetCore.StaticFiles;

namespace notes.Pages.Editor;

public enum MediaKind { None, Image, Video, Audio, Pdf }

/// <summary>Which files the browser can be asked to show as media, going by the extension (ASP.NET's own mapping of extensions to media types).</summary>
public static class MediaTypes
{
    private static readonly FileExtensionContentTypeProvider Provider = new();

    /// <summary>
    /// The media type and kind of a file the browser may show inline, or false for everything else. SVG is left out
    /// on purpose: it can carry scripts, and serving it inline from this origin would run them.
    /// </summary>
    public static bool TryGet(string path, out string contentType, out MediaKind kind)
    {
        kind = MediaKind.None;
        if (!Provider.TryGetContentType(path, out string? type) || type == "image/svg+xml")
        {
            contentType = "";
            return false;
        }

        contentType = type;
        kind = type switch
        {
            "application/pdf" => MediaKind.Pdf,
            _ when type.StartsWith("image/") => MediaKind.Image,
            _ when type.StartsWith("video/") => MediaKind.Video,
            _ when type.StartsWith("audio/") => MediaKind.Audio,
            _ => MediaKind.None
        };
        return kind != MediaKind.None;
    }
}
