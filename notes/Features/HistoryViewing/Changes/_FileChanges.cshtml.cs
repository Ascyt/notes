using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Components.Breadcrumb;
using notes.Features.Components.Toolbar;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Views;
using notes.Pages.Editor;

namespace notes.Features.HistoryViewing.Changes;

/// <summary>A side of the change of a file, as the page shows it.</summary>
/// <param name="Text">Its contents, where it is shown and is text.</param>
/// <param name="Media">What a binary side is shown as.</param>
/// <param name="RawBytesQuery">The query of its bytes, which the browser shows media from.</param>
public sealed record FileChangeSide(ChangeSideKind Kind, bool IsBinary, string Text, MediaKind Media, string FormattedSize, string RawBytesQuery)
{
    public bool IsShown => Kind == ChangeSideKind.Shown;

    /// <summary>The kind as the page names it (<c>data-side-kind</c>), for its scripts and tests.</summary>
    public string KindName => Kind switch
    {
        ChangeSideKind.Shown => "shown",
        ChangeSideKind.NotThere => "not-there",
        _ => "not-shown"
    };
}

/// <summary>What the script of the page (file-changes.js) is told: the two sides of text, or the one that is shown.</summary>
/// <param name="ShowsBoth">Whether both sides are shown in the diff editor; otherwise only <paramref name="Modified"/>, in the read-only editor.</param>
/// <param name="Original">The text before the commit ("" where it wasn't there).</param>
/// <param name="Modified">The text as the commit left it, or the only side that is shown.</param>
public sealed record FileChangesOptions(bool ShowsBoth, string Original, string Modified, string Language, string MonacoPath, string ReadOnlyMessage);

/// <summary>The changes of a commit to a file: the two sides, in the diff editor for text, next to each other otherwise.</summary>
public class _FileChanges : PageModel
{
    public string VirtualPath { get; init; } = "";
    public FileChangeSide Before { get; init; } = null!;
    public FileChangeSide After { get; init; } = null!;

    /// <summary>The sidebar of the list of changes, shown from the start as in the view of a commit.</summary>
    public _CommitSidebar? CommitSidebar { get; init; }

    /// <summary>The way to the state of the commit.</summary>
    public ToolbarButton CommitChangesButton { get; init; } = null!;

    /// <summary>The directories above that can't be seen as they were, for the breadcrumb.</summary>
    public IReadOnlyDictionary<string, ParentWithoutHistory> ParentsWithoutHistory { get; init; } = new Dictionary<string, ParentWithoutHistory>();

    /// <summary>Whether it is text on every side that is shown, which the diff editor shows; otherwise the sides are shown next to each other.</summary>
    public bool ShowsText => !Before.IsBinary && !After.IsBinary;

    public string Language => FileView.LanguageOf(VirtualPath);

    /// <summary>Where only one side of text is shown, why the other one isn't; null where both are.</summary>
    public string? OneSideNotShownReason => Before.Kind == ChangeSideKind.NotShown
        ? "The file can't be seen as it was before this commit."
        : After.Kind == ChangeSideKind.NotShown ? "The file can't be seen as this commit left it." : null;

    public FileChangesOptions EditorOptions(string monacoPath)
        => OneSideNotShownReason is null
            ? new FileChangesOptions(ShowsBoth: true, Before.Text, After.Text, Language, monacoPath, EarlierStateViews.CantBeChangedReason)
            : new FileChangesOptions(ShowsBoth: false, "", Before.IsShown ? Before.Text : After.Text, Language, monacoPath, EarlierStateViews.CantBeChangedReason);
}
