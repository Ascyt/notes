using notes.Pages.Services;

namespace notes.Jobs;

/// <summary>Removes the leftovers of uploads that were abandoned half way (closed tab, lost connection, ...).</summary>
public sealed class ClearStaleUploadsJob(IFileService fileService, ILogger<ClearStaleUploadsJob> logger)
{
    public const string Id = "clear-stale-uploads";

    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    public void Run()
    {
        int deleted = fileService.DeleteStaleUploads(MaxAge);
        logger.LogInformation("Deleted {count} stale upload temp file(s)", deleted);
    }
}
