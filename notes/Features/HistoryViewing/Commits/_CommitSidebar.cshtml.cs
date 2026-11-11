using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.HistoryViewing.Commits;

/// <summary>The list of the changes of the current file or directory, at the left of the page, behind the changes button of the toolbar.</summary>
public class _CommitSidebar : PageModel
{
    /// <summary>The query parameter that asks for the commits that changed the entry (as the HTML of <see cref="_CommitList"/>), which the sidebar shows.</summary>
    public const string CommitListParameter = "commits";

    /// <summary>Which page of them (from 1, the newest), with <see cref="CommitListParameter"/>.</summary>
    public const string PageParameter = "page";

    /// <summary>
    /// Asks for the commits right after the state that is shown instead of a page from it back (as the HTML of <see cref="_CommitLines"/>),
    /// with <see cref="CommitListParameter"/>.
    /// </summary>
    public const string NewerParameter = "newer";

    /// <summary>What is looked at ("file" or "directory"), used in the text.</summary>
    public string Kind { get; init; } = "";

    /// <summary>
    /// In the view of a commit (<c>?commit=</c>): the address of the entry as it is now, which the × of the sidebar leads to. The sidebar is
    /// always shown there (where there is room for it), with <see cref="Contents"/>; elsewhere it is asked for when it is first shown.
    /// </summary>
    public string? AddressOfNow { get; init; }

    /// <summary>What the sidebar shows from the start (in the view of a commit), or null.</summary>
    public _CommitList? Contents { get; init; }

    public bool IsCommitView => AddressOfNow is not null;
}
