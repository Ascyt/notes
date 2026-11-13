namespace notes.Features.HistoryViewing.Views;

/// <summary>Whether a page has the history button (the way to the earlier states of its entry), and whether it can be used.</summary>
/// <param name="IsShown">Whether the button is there at all.</param>
/// <param name="DisabledReason">Why it is there but can't be used, if so.</param>
public sealed record HistoryButtonState(bool IsShown, string? DisabledReason = null)
{
    public static HistoryButtonState Hidden { get; } = new(false);

    public static HistoryButtonState Usable { get; } = new(true);

    /// <summary>The button is there and can be used, so the page needs the dialog behind it.</summary>
    public bool CanBeUsed => IsShown && DisabledReason is null;
}
