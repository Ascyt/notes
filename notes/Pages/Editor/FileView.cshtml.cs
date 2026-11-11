using System.Buffers;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Components.Breadcrumb;
using notes.Features.HistoryViewing;
using notes.Pages.Editor.Markdown;

namespace notes.Pages.Editor;

/// <summary>What the editor's script (file-editor.js) is told about the file.</summary>
/// <param name="CanWrite">Whether it may be changed; otherwise the editor is read-only and Save stays disabled.</param>
/// <param name="MonacoPath">Where Monaco is served from.</param>
/// <param name="ReadOnlyMessage">What the editor says when typing into it while it is read-only.</param>
public sealed record FileEditorOptions(bool CanWrite, string Contents, string Language, string MonacoPath, string ReadOnlyMessage);

/// <summary>A file as an earlier state has it (<c>?before=</c>), shown instead of the one on disk.</summary>
/// <param name="Before">The time of the state, as the address has it.</param>
public sealed record EarlierFile(DateTimeOffset Before, long Size, Func<Stream> OpenContents);

public class FileView : PageModel
{
    /// <summary>How much of the start of a file is looked at to tell text from binary (the same amount git looks at).</summary>
    private const int SniffLength = 8000;

    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";

    /// <summary>Whether the user may change the contents. Without it the editor is read-only and there is no saving.</summary>
    public bool CanWrite { get; init; }

    /// <summary>The file as an earlier state has it, or null for the file on disk. Nothing of it can be changed.</summary>
    public EarlierFile? Earlier { get; init; }

    public bool ShowsEarlierState => Earlier is not null;

    /// <summary>Why the contents can't be changed, which the editor says when typed into.</summary>
    public string ReadOnlyReason => ShowsEarlierState ? EarlierStateViews.CantBeChangedReason : "Insufficient permissions to change this file.";

    /// <summary>The query of the raw bytes of the file (of the state that is shown).</summary>
    public string RawBytesQuery => ShowsEarlierState ? "raw&" + _HistoryModal.BeforeParameter + "=" + EarlierStateTime.Format(Earlier!.Before) : "raw";

    /// <summary>Whether the user may manage permissions, so the page has the way to the permissions of the file.</summary>
    public bool CanConfigure { get; init; }

    /// <summary>Whether the page has the way to the earlier states of the file.</summary>
    public HistoryButtonState HistoryButton { get; init; } = HistoryButtonState.Hidden;

    /// <summary>In an earlier state: the directories above that can't be seen as they were, for the breadcrumb.</summary>
    public IReadOnlyDictionary<string, ParentWithoutHistory> ParentsWithoutHistory { get; init; } = new Dictionary<string, ParentWithoutHistory>();

    /// <summary>Why the user can't delete the file, or null if they can.</summary>
    public string? DeleteDeniedReason { get; init; }

    /// <summary>Why the user can't rename the file, or null if they can.</summary>
    public string? RenameDeniedReason { get; init; }

    /// <summary>
    /// Whether the user can't change anything about the file: not its contents, nor delete it. The page then doesn't offer the buttons that
    /// change things (save, delete) at all, instead of showing them disabled.
    /// </summary>
    public bool IsReadOnly => !CanWrite && DeleteDeniedReason is not null;

    /// <summary>Whether the address asked for the editor (<c>?edit</c>). Only a markdown file has anything else to show.</summary>
    public bool EditRequested { get; init; }

    public string Contents { get; private set; } = "";
    public bool IsBinary { get; private set; }

    /// <summary>Whether the file is markdown, which is shown rendered unless it is being edited.</summary>
    public bool IsMarkdown => !IsBinary && Path.GetExtension(VirtualPath).Equals(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the page shows the rendered markdown instead of the editor. That is the default for a markdown file, and all there is for
    /// whoever may not change it (asking for the editor gets them the preview). Any other text is only ever shown in the editor.
    /// </summary>
    public bool ShowsPreview => IsMarkdown && !(EditRequested && CanWrite);

    /// <summary>The host the page is served from, which links in the rendered markdown lead to without opening a new tab.</summary>
    public string SiteHost { get; init; } = "";

    /// <summary>The rendered markdown. Only made when <see cref="ShowsPreview"/>.</summary>
    public RenderedMarkdown Preview { get; private set; } = new("", HasMath: false, HasDiagrams: false, HasAbbreviations: false);
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
        Size = Earlier?.Size ?? new FileInfo(PhysicalPath).Length;

        if (await LooksBinaryAsync())
        {
            IsBinary = true;
            _ = MediaTypes.TryGet(PhysicalPath, out _, out MediaKind kind);
            Media = kind;
            return;
        }

        using (StreamReader reader = new(OpenContents(), detectEncodingFromByteOrderMarks: true))
            Contents = await reader.ReadToEndAsync();
        if (ShowsPreview)
            Preview = MarkdownRenderer.Render(Contents, SiteHost, RawBytesQuery);
    }

    /// <summary>The contents of the file that is shown: as the earlier state has it, or on disk.</summary>
    private Stream OpenContents()
        => Earlier?.OpenContents() ?? new FileStream(PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    /// <summary>A file is binary if its start has a NUL byte or isn't valid UTF-8, which is all the editor can show.</summary>
    private async Task<bool> LooksBinaryAsync()
    {
        byte[] buffer = new byte[SniffLength];
        int length;
        await using (Stream stream = OpenContents())
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
