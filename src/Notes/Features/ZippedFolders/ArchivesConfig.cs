namespace Notes.Features.ZippedFolders;

/// <summary>How archived folders behave (<c>Config:Archives</c> in appsettings). See docs/administration/archived-directories.md.</summary>
public sealed class ArchivesConfig
{
    /// <summary>For how many minutes an unzipped folder stays unzipped after it was last used, before it is zipped again.</summary>
    public double ZipAfterMinutes { get; init; } = 30;

    /// <summary>
    /// How long a request waits for an archived folder to be unzipped, in seconds, before the page shows how far it got instead: a small one
    /// is unzipped before the user notices.
    /// </summary>
    public double WaitSeconds { get; init; } = 1;

    /// <summary>
    /// How many times what was in an encrypted folder while it was unzipped is overwritten with random data before it is deleted (also what
    /// a crash left in the work of zipping, which may have been). 0 only deletes it.
    /// </summary>
    public int OverwritePasses { get; init; } = 1;

    /// <summary>Throws if a setting makes no sense, so that the app doesn't start with it.</summary>
    public void Check()
    {
        if (OverwritePasses is < 0 or > 35)
            throw new InvalidOperationException("Config:Archives:OverwritePasses must be from 0 to 35.");
        if (ZipAfterMinutes <= 0)
            throw new InvalidOperationException("Config:Archives:ZipAfterMinutes must be more than 0.");
        if (WaitSeconds < 0)
            throw new InvalidOperationException("Config:Archives:WaitSeconds can't be negative.");
    }
}
