namespace Notes.Features.ZippedFolders;

/// <summary>Zips the archived folders that are unzipped and weren't used for a while (<see cref="ArchivesConfig.ZipAfterMinutes"/>).</summary>
public sealed class ZipIdleArchivesJob(ArchivedFolders archives, ILogger<ZipIdleArchivesJob> logger)
{
    public const string Id = "zip-idle-archives";

    public async Task RunAsync()
    {
        int zipped = await archives.ZipIdleAsync();
        if (zipped > 0)
            logger.LogInformation("Zipped {count} archived folder(s) that weren't used for a while", zipped);
    }
}
