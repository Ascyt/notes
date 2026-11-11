using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.HistoryViewing;

/// <summary>The card at the bottom of a page of an earlier state: which time is shown, and the way back to now.</summary>
public class _EarlierStateCard : PageModel
{
    /// <summary>The time of the state that is shown.</summary>
    public DateTimeOffset Before { get; init; }

    /// <summary>The same page without <c>?before=</c>: the entry as it is now.</summary>
    public string BackToNowHref { get; init; } = "";
}
