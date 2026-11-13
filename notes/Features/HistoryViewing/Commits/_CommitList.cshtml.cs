using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.HistoryViewing.Commits;

/// <summary>What the sidebar of the list of changes shows: a page of the commits, and the way to the other pages.</summary>
public class _CommitList : PageModel
{
    public _CommitLines Lines { get; init; } = new();

    /// <summary>Which page it is (from 1, the newest).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Whether there is a next, older page.</summary>
    public bool HasOlder { get; init; }

    /// <param name="shownCommitId">The commit that is shown (<c>?commit=</c>), if any, which the list marks.</param>
    public static _CommitList Of(CommitList list, string? shownCommitId)
        => new()
        {
            Lines = _CommitLines.Of(list, shownCommitId),
            Page = list.Page,
            HasOlder = list.HasOlder
        };
}
