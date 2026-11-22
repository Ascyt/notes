namespace Notes.Features.Components.Archive;

/// <summary>Deletes the archives that were made to be downloaded once they are an hour old, downloaded or not.</summary>
public sealed class ClearStaleArchivesJob(PreparedArchives archives, ILogger<ClearStaleArchivesJob> logger)
{
    public const string Id = "clear-stale-archives";

    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    public void Run()
    {
        int deleted = archives.DeleteStale(MaxAge);
        if (deleted > 0)
            logger.LogInformation("Deleted {count} stale archive(s)", deleted);
    }
}
