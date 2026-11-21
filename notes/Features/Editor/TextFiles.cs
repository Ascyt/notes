using System.Buffers;
using System.Text.Unicode;

namespace notes.Features.Editor;

/// <summary>What the editor can show of a file: whether it is text at all, and how it is highlighted.</summary>
public static class TextFiles
{
    /// <summary>How much of the start of a file is looked at to tell text from binary (the same amount git looks at).</summary>
    private const int SniffLength = 8000;

    /// <summary>How the editor highlights a file of that name.</summary>
    public static string LanguageOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" => "markdown",
        ".json" => "json",
        _ => "plaintext"
    };

    /// <summary>
    /// What is in the stream, or null if it is more than <paramref name="maxBytes"/> bytes: no more than that is read into memory, however
    /// large the stream is (or grows while it is read).
    /// </summary>
    public static async Task<byte[]?> ReadAtMostAsync(Stream stream, int maxBytes)
    {
        using MemoryStream bytes = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (bytes.Length + read > maxBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }

        return bytes.ToArray();
    }

    /// <summary>A file is binary if its start has a NUL byte or isn't valid UTF-8, which is all the editor can show.</summary>
    public static async Task<bool> LooksBinaryAsync(Stream stream)
    {
        byte[] buffer = new byte[SniffLength];
        int length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);

        ReadOnlySpan<byte> bytes = buffer.AsSpan(0, length);
        if (bytes.Contains((byte)0))
            return true;

        // The cut-off may fall inside a character, which only counts as invalid if the file ends there
        char[] chars = new char[length];
        OperationStatus status = Utf8.ToUtf16(bytes, chars, out _, out _, replaceInvalidSequences: false, isFinalBlock: length < SniffLength);
        return status == OperationStatus.InvalidData;
    }
}
