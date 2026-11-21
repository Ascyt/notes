namespace Notes;

/// <summary>
/// When the background jobs run (<c>Config:Jobs</c> in appsettings), as cron expressions (minute, hour, day of the month, month, day of
/// the week). See docs/administration/configuration.md. A schedule that isn't one doesn't let the app start.
/// </summary>
public sealed class JobsConfig
{
    /// <summary>When the unfinished uploads that were left for an hour are discarded. Every hour by default.</summary>
    public string ClearStaleUploads { get; init; } = "0 * * * *";

    /// <summary>
    /// When the history looks for changes that were left alone long enough to be committed. Every minute by default. How long that is, is
    /// <c>History:IdleMinutes</c>; this is only how often it is looked at.
    /// </summary>
    public string CommitIdleChanges { get; init; } = "* * * * *";
}
