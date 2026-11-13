using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.HistoryViewing.Changes;

/// <summary>The mark after an entry of the listing of the changes of a commit: what the commit did to it, in the colors of git.</summary>
public class _ChangeMark : PageModel
{
    public ChangeMark Mark { get; init; }

    public (string Icon, string Color, string Label) Look => Mark switch
    {
        ChangeMark.Added => ("fa-plus", "text-success", "Added by this commit"),
        ChangeMark.Deleted => ("fa-minus", "text-danger", "Deleted by this commit"),
        _ => ("fa-pen", "text-warning", "Changed by this commit")
    };
}
