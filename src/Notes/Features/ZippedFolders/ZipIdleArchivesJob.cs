using Notes.Features.Helpers;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// Zips the archived folders that are unzipped and weren't used for a while (<see cref="ArchivesConfig.ZipAfterMinutes"/>). First it brings
/// what is on disk in line with the configurations where that can change without a configuration being saved: folders below an
/// <c>onLowestLevel</c> one that came to hold files are archived, and ones whose archiving was turned off while they were in use are undone.
/// </summary>
public sealed class ZipIdleArchivesJob(ArchivedFolders archives, StructureLock structureLock, ILogger<ZipIdleArchivesJob> logger)
{
    public const string Id = "zip-idle-archives";

    public async Task RunAsync()
    {
        // (Folders are moved: not while an operation of a user moves things)
        using (await structureLock.EnterAsync())
            await archives.ReconcileLowestLevelsAsync();

        int zipped = await archives.ZipIdleAsync();
        if (zipped > 0)
            logger.LogInformation("Zipped {count} archived folder(s) that weren't used for a while", zipped);
    }
}
