using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Components.Breadcrumb;
using notes.Features.Components.Toolbar;
using notes.Features.Editor;
using notes.Features.Helpers;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Requests;
using notes.Features.HistoryViewing.Views;
using notes.Features.Markdown;
using notes.Features.Media;

namespace notes.Pages.Editor;

/// <summary>What the editor's script (file-editor.js) is told about the file.</summary>
/// <param name="CanWrite">Whether it may be changed; otherwise the editor is read-only and Save stays disabled.</param>
/// <param name="MonacoPath">Where Monaco is served from.</param>
/// <param name="ReadOnlyMessage">What the editor says when typing into it while it is read-only.</param>
/// <param name="SuggestsWords">Whether the code editor suggests words of the text while typing, which is left out for prose.</param>
public sealed record FileEditorOptions(bool CanWrite, string Contents, string Language, string MonacoPath, string ReadOnlyMessage, bool SuggestsWords);

/// <summary>A file as an earlier state has it (<c>?before=</c> or <c>?commit=</c>), shown instead of the one on disk.</summary>
/// <param name="Shown">Which state it is, as the address has it.</param>
public sealed record EarlierFile(EarlierStateParameter Shown, long Size, Func<Stream> OpenContents);

public class FileView : PageModel
{
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
    public string RawBytesQuery => ShowsEarlierState ? "raw&" + Earlier!.Shown.QueryPart : "raw";

    /// <summary>Whether the user may manage permissions, so the page has the way to the permissions of the file.</summary>
    public bool CanConfigure { get; init; }

    /// <summary>
    /// For an image, video or audio file: the media before and after it in the grid of its directory (in the state that is shown), which the toolbar leads
    /// to. Null for any other file.
    /// </summary>
    public Browse.MediaNeighbours? MediaNeighbours { get; init; }

    /// <summary>Whether the page has the way to the earlier states of the file.</summary>
    public HistoryButtonState HistoryButton { get; init; } = HistoryButtonState.Hidden;

    /// <summary>In the view of a commit, the sidebar of the list of changes with what it shows from the start; otherwise null (it starts empty).</summary>
    public _CommitSidebar? CommitSidebar { get; init; }

    /// <summary>In the view of a commit, the way to what it changed of the file (<see cref="ToolbarButton.CommitChanges"/>); otherwise null.</summary>
    public ToolbarButton? CommitChangesButton { get; init; }

    /// <summary>In an earlier state: the directories above that can't be seen as they were, for the breadcrumb.</summary>
    public IReadOnlyDictionary<string, ParentWithoutHistory> ParentsWithoutHistory { get; init; } = new Dictionary<string, ParentWithoutHistory>();

    /// <summary>Why the user can't delete the file, or null if they can.</summary>
    public string? DeleteDeniedReason { get; init; }

    /// <summary>Why the user can't rename the file, or null if they can.</summary>
    public string? RenameDeniedReason { get; init; }

    /// <summary>
    /// Whether the user can't change anything about the file: not its contents, nor delete it. The page then doesn't offer the buttons that
    /// change things (save) at all, instead of showing them disabled.
    /// </summary>
    public bool IsReadOnly => !CanWrite && DeleteDeniedReason is not null;

    /// <summary>Whether the address asked for the editor (<c>?edit</c>). Only a markdown file has anything else to show.</summary>
    public bool EditRequested { get; init; }

    /// <summary>The largest text that is shown (<see cref="LimitsConfig.MaxTextBytes"/>).</summary>
    public int MaxTextBytes { get; init; } = new LimitsConfig().MaxTextBytes;

    public string Contents { get; private set; } = "";

    /// <summary>Whether the file is shown as one whose contents can't be read as text: it is binary, or text too large to be shown (<see cref="IsTooLarge"/>).</summary>
    public bool IsBinary { get; private set; }

    /// <summary>Whether the file is text, but larger than <see cref="MaxTextBytes"/>: reading it into the page would take the server's memory.</summary>
    public bool IsTooLarge { get; private set; }

    /// <summary>Whether the file is markdown, which is shown rendered unless it is being edited.</summary>
    public bool IsMarkdown => !IsBinary && Path.GetExtension(VirtualPath).Equals(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the page shows the rendered markdown instead of the editor. That is the default for a markdown file; asking for the editor
    /// shows its source, read-only for whoever may not change it (and in an earlier state). Any other text is only ever shown in the editor.
    /// </summary>
    public bool ShowsPreview => IsMarkdown && !EditRequested;

    /// <summary>The host the page is served from, which links in the rendered markdown lead to without opening a new tab.</summary>
    public string SiteHost { get; init; } = "";

    /// <summary>The rendered markdown. Only made when <see cref="ShowsPreview"/>.</summary>
    public RenderedMarkdown Preview { get; private set; } = new("", HasMath: false, HasDiagrams: false, HasAbbreviations: false);
    public long Size { get; private set; }
    /// <summary>What a binary file is shown as. <see cref="MediaKind.None"/> if the browser has nothing to show it with.</summary>
    public MediaKind Media { get; private set; }

    public string Language => TextFiles.LanguageOf(VirtualPath);

    /// <summary>Text written as prose (notes, plain text), where the editor's suggestions of words are more in the way than of help.</summary>
    public bool IsProse => Path.GetExtension(VirtualPath).ToLowerInvariant() is ".md" or ".txt";

    public string FormattedSize => ByteSizes.Format(Size);

    public async Task InitAsync()
    {
        Size = Earlier?.Size ?? new FileInfo(PhysicalPath).Length;

        bool looksBinary = await LooksBinaryAsync();
        // The file may grow after its size was looked at: what is read stops at the limit too
        byte[]? bytes = looksBinary || Size > MaxTextBytes ? null : await ReadAtMostAsync();
        if (bytes is null)
        {
            IsBinary = true;
            IsTooLarge = !looksBinary;
            Media = MediaTypes.KindOf(PhysicalPath);
            return;
        }

        using (StreamReader reader = new(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true))
            Contents = await reader.ReadToEndAsync();
        if (ShowsPreview)
            Preview = MarkdownRenderer.Render(Contents, SiteHost, RawBytesQuery);
    }

    /// <summary>The contents of the file that is shown: as the earlier state has it, or on disk (which a save may replace meanwhile).</summary>
    private Stream OpenContents()
        => Earlier?.OpenContents() ?? new FileStream(PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private async Task<bool> LooksBinaryAsync()
    {
        await using Stream stream = OpenContents();
        return await TextFiles.LooksBinaryAsync(stream);
    }

    private async Task<byte[]?> ReadAtMostAsync()
    {
        await using Stream stream = OpenContents();
        return await TextFiles.ReadAtMostAsync(stream, MaxTextBytes);
    }
}
