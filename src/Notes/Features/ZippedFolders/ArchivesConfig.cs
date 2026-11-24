namespace Notes.Features.ZippedFolders;

/// <summary>How archived folders behave (<c>Config:Archives</c> in appsettings). See docs/administration/archived-directories.md.</summary>
public sealed class ArchivesConfig
{
    /// <summary>For how many minutes an unzipped folder stays unzipped after it was last used, before it is zipped again.</summary>
    public double ZipAfterMinutes { get; init; } = 10;

    /// <summary>
    /// How long a request waits for an archived folder to be unzipped, in seconds, before the page shows how far it got instead: a small one
    /// is unzipped before the user notices.
    /// </summary>
    public double WaitSeconds { get; init; } = 1.5;

    /// <summary>Throws if a setting makes no sense, so that the app doesn't start with it.</summary>
    public void Check()
    {
        if (ZipAfterMinutes <= 0)
            throw new InvalidOperationException("Config:Archives:ZipAfterMinutes must be more than 0.");
        if (WaitSeconds < 0)
            throw new InvalidOperationException("Config:Archives:WaitSeconds can't be negative.");
    }
}
