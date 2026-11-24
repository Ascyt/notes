namespace Notes.Features.History;

/// <summary>When the history commits (<c>Config:History</c> in appsettings). See docs/administration/history.md.</summary>
public sealed class HistoryConfig
{
    public bool Enabled { get; init; } = true;

    /// <summary>After how many minutes without a change in the app what is pending is committed.</summary>
    public double IdleMinutes { get; init; } = 10;

    /// <summary>
    /// How many changed lines (added ones, and removed ones times <see cref="RemovedLineWeight"/>) since the last commit are committed right away.
    /// </summary>
    public double LineThreshold { get; init; } = 50;

    /// <summary>
    /// How much a removed line counts towards <see cref="LineThreshold"/>, compared to an added one: what is removed is gone from the entries.
    /// </summary>
    public double RemovedLineWeight { get; init; } = 2;

    /// <summary>
    /// Files larger than this (in megabytes) are left out of the history, so that it doesn't grow by every version of a big binary file; null
    /// keeps them, whatever their size. Configuration files are always kept.
    /// </summary>
    public double? MaxFileMegabytes { get; init; } = 50;

    /// <summary>Whether the file (physical path) is larger than <see cref="MaxFileMegabytes"/> now. False for one that isn't there.</summary>
    public bool IsTooBig(string physicalPath)
    {
        if (MaxFileMegabytes is not { } megabytes)
            return false;
        FileInfo file = new(physicalPath);
        return file.Exists && file.Length > (long)(megabytes * 1024 * 1024);
    }
}
