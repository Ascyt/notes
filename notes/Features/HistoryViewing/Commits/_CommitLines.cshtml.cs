using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.HistoryViewing.Commits;

/// <summary>Commits of the list of changes, one line each, and above them the way to newer ones: a page of them, or the newer ones added on top.</summary>
public class _CommitLines : PageModel
{
    /// <summary>The newest first.</summary>
    public IReadOnlyList<CommitSummary> Commits { get; init; } = [];

    /// <summary>The commit that is shown (<c>?commit=</c>), marked in the list.</summary>
    public string? ShownCommitId { get; init; }

    /// <summary>The commit after which the list has newer ones, which "Show newer…" asks for; null if there are none.</summary>
    public string? NewerAfter { get; init; }

    /// <param name="shownCommitId">The commit that is shown (<c>?commit=</c>), if any, which the list marks.</param>
    public static _CommitLines Of(CommitList list, string? shownCommitId)
        => new() { Commits = list.Commits, ShownCommitId = shownCommitId, NewerAfter = list.NewerAfter };
}
