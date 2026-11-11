using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Requests;

namespace notes.Features.HistoryViewing.Views;

/// <summary>The card at the bottom of a page of an earlier state: which state is shown, and the way back to now.</summary>
public class _EarlierStateCard : PageModel
{
    /// <summary>What in the address says which state is shown, which the card's script keeps on the links of the page.</summary>
    public EarlierStateParameter Shown { get; init; } = EarlierStateParameter.ForTime(DateTimeOffset.UnixEpoch);

    /// <summary>For <c>?before=</c> the time asked for, for <c>?commit=</c> when the commit was made.</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>The commit that is shown (<c>?commit=</c>), if the user may know of it: then the card also says what it was and who made it.</summary>
    public CommitSummary? Commit { get; init; }

    /// <summary>The same page without the earlier state: the entry as it is now.</summary>
    public string BackToNowHref { get; init; } = "";
}
