namespace Notes.Features.Components.Archive;

public enum ArchiveFormat
{
    SevenZip,
    Zip
}

/// <summary>How hard the archive is compressed: the larger, the smaller the archive and the longer it takes.</summary>
public enum ArchiveCompression
{
    /// <summary>Not compressed at all. Only for zip: the 7z writer always compresses, so 7z has <see cref="Min"/> in its place.</summary>
    Store,

    /// <summary>As little and as fast as 7z compresses (only for 7z).</summary>
    Min,
    Medium,
    High,
    Max
}

/// <summary>What the archive options dialog asks for (<see cref="_ArchiveModal"/>).</summary>
/// <param name="Password">What the archive is encrypted with, or null for none.</param>
public sealed record ArchiveOptions(ArchiveFormat Format, ArchiveCompression Compression, string? Password)
{
    /// <summary>A password typed into a dialog is far shorter; this only keeps what is sent bounded.</summary>
    public const int MaxPasswordLength = 256;

    /// <summary>The extension of the file, with its dot.</summary>
    public string Extension => Format == ArchiveFormat.SevenZip ? ".7z" : ".zip";

    /// <summary>
    /// The options as the dialog sends them (<c>"7z"</c> or <c>"zip"</c>; <c>"store"</c> for zip or <c>"min"</c> for 7z, <c>"medium"</c>,
    /// <c>"high"</c> or <c>"max"</c>; a password or nothing), or null if any of them is none of these. An empty password is no password.
    /// </summary>
    public static ArchiveOptions? Parse(string? format, string? compression, string? password)
    {
        ArchiveFormat? parsedFormat = format switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            _ => null
        };
        ArchiveCompression? parsedCompression = compression switch
        {
            "store" => ArchiveCompression.Store,
            "min" => ArchiveCompression.Min,
            "medium" => ArchiveCompression.Medium,
            "high" => ArchiveCompression.High,
            "max" => ArchiveCompression.Max,
            _ => null
        };
        if (parsedFormat is not { } chosenFormat || parsedCompression is not { } chosenCompression)
            return null;

        // The 7z writer can't store without compressing, and the lowest level is the one of 7z
        if ((chosenFormat == ArchiveFormat.SevenZip) != (chosenCompression == ArchiveCompression.Min)
            && chosenCompression is ArchiveCompression.Store or ArchiveCompression.Min)
            return null;

        // Control characters can't be typed into the field, and would be read differently by the programs that open the archive
        if (password is not null && (password.Length > MaxPasswordLength || password.Any(char.IsControl)))
            return null;

        return new ArchiveOptions(chosenFormat, chosenCompression, string.IsNullOrEmpty(password) ? null : password);
    }
}
