namespace notes.Features.Components.Upload;

/// <summary>Removes the leftovers of uploads that were abandoned half way (closed tab, lost connection, ...).</summary>
public sealed class ClearStaleUploadsJob(IUploadService uploadService, ILogger<ClearStaleUploadsJob> logger)
{
    public const string Id = "clear-stale-uploads";

    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    public void Run()
    {
        int deleted = uploadService.DeleteStaleUploads(MaxAge);
        if (deleted > 0)
            logger.LogInformation("Deleted {count} stale upload temp file(s)", deleted);
    }
}
