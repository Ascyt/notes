using System.Buffers;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.View.Editor;

public class FileView : PageModel
{
    /// <summary>How much of the start of a file is looked at to tell text from binary (the same amount git looks at).</summary>
    private const int SniffLength = 8000;

    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";

    /// <summary>Whether the user may change the contents. Without it the editor is read-only and there is no saving.</summary>
    public bool CanWrite { get; init; }

    /// <summary>Why the user can't delete the file, or null if they can.</summary>
    public string? DeleteDeniedReason { get; init; }

    /// <summary>Why the user can't rename the file, or null if they can.</summary>
    public string? RenameDeniedReason { get; init; }

    public string Contents { get; private set; } = "";
    public bool IsBinary { get; private set; }
    public long Size { get; private set; }
    /// <summary>What a binary file is shown as. <see cref="MediaKind.None"/> if the browser has nothing to show it with.</summary>
    public MediaKind Media { get; private set; }

    public string Language => Path.GetExtension(VirtualPath).ToLowerInvariant() switch
    {
        ".md" => "markdown",
        ".json" => "json",
        _ => "plaintext"
    };

    public string FormattedSize
    {
        get
        {
            string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
            double size = Size;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0 ? $"{Size} B" : $"{size:0.#} {units[unit]}";
        }
    }

    public async Task InitAsync()
    {
        Size = new FileInfo(PhysicalPath).Length;

        if (await LooksBinaryAsync())
        {
            IsBinary = true;
            _ = MediaTypes.TryGet(PhysicalPath, out _, out MediaKind kind);
            Media = kind;
            return;
        }

        Contents = await System.IO.File.ReadAllTextAsync(PhysicalPath);
    }

    /// <summary>A file is binary if its start has a NUL byte or isn't valid UTF-8, which is all the editor can show.</summary>
    private async Task<bool> LooksBinaryAsync()
    {
        byte[] buffer = new byte[SniffLength];
        int length;
        await using (FileStream stream = new(PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);

        ReadOnlySpan<byte> bytes = buffer.AsSpan(0, length);
        if (bytes.Contains((byte)0))
            return true;

        // The cut-off may fall inside a character, which only counts as invalid if the file ends there
        char[] chars = new char[length];
        OperationStatus status = Utf8.ToUtf16(bytes, chars, out _, out _, replaceInvalidSequences: false, isFinalBlock: length < SniffLength);
        return status == OperationStatus.InvalidData;
    }
}
