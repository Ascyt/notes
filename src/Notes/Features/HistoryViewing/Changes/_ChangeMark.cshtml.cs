using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.HistoryViewing.Changes;

/// <summary>The mark after an entry of the listing of the changes of a commit: what the commit did to it, in the colors of git.</summary>
public class _ChangeMark : PageModel
{
    public ChangeMark Mark { get; init; }

    public (string Icon, string Color) Look => Mark switch
    {
        ChangeMark.Added => ("fa-plus", "text-success"),
        ChangeMark.Deleted => ("fa-minus", "text-danger"),
        _ => ("fa-pen", "text-warning")
    };
}
