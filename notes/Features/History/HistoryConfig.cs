namespace notes.Features.History;

/// <summary>When the history of the notes commits (<c>Config:History</c> in appsettings). See docs/administration/history.md.</summary>
public sealed class HistoryConfig
{
    public bool Enabled { get; init; } = true;

    /// <summary>After how many minutes without a change in the app what is pending is committed.</summary>
    public double IdleMinutes { get; init; } = 10;

    /// <summary>
    /// How many changed lines (added ones, and removed ones times <see cref="RemovedLineWeight"/>) since the last commit are committed right away.
    /// </summary>
    public double LineThreshold { get; init; } = 200;

    /// <summary>How much a removed line counts towards <see cref="LineThreshold"/>, compared to an added one: what is removed is gone from the notes.</summary>
    public double RemovedLineWeight { get; init; } = 2;
}
