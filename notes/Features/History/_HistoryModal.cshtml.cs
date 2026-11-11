using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Features.History;

/// <summary>Whether a page has the way to the earlier states of its entry (the History button and its dialog).</summary>
/// <param name="IsOffered">Whether the button is there at all.</param>
/// <param name="DisabledReason">Why it is there but can't be used, if so.</param>
public sealed record HistoryOffer(bool IsOffered, string? DisabledReason = null)
{
    public static HistoryOffer None { get; } = new(false);

    public static HistoryOffer Available { get; } = new(true);

    /// <summary>The button is there and can be used, so the page needs the dialog behind it.</summary>
    public bool IsUsable => IsOffered && DisabledReason is null;
}

/// <summary>The choice of an earlier state of the current file or directory, behind the History button of the toolbar.</summary>
public class _HistoryModal : PageModel
{
    /// <summary>The query parameter that says which state of the notes is shown: the last one before this time (UTC, ISO 8601).</summary>
    public const string BeforeParameter = "before";

    /// <summary>What is looked at ("file" or "directory"), used in the text.</summary>
    public string Kind { get; init; } = "";
}
