namespace Notes.Features.Helpers;

/// <summary>
/// What long work (zipping and unzipping archived folders, making an archive to download, unzipping an upload) is doing now, for the text under
/// its bar: the step (<see cref="WorkPhases"/>), and how much of it is done where that is counted. The pages say it in words
/// (<c>describeProgress</c> in site.ts, with <see cref="Components.Busy.ProgressTexts"/>); a count of 0 of 0 is left out.
/// </summary>
/// <param name="Phase">One of <see cref="WorkPhases"/>.</param>
/// <param name="WaitingFor">While it is <see cref="WorkPhases.Waiting"/>: the title of the operation it waits for, where that is known.</param>
public sealed record WorkStatus(string Phase, int Files = 0, int TotalFiles = 0, long Bytes = 0, long TotalBytes = 0, string? WaitingFor = null);

/// <summary>The steps of <see cref="WorkStatus"/>, as the pages know them.</summary>
public static class WorkPhases
{
    /// <summary>Waiting for its turn (others are unzipped first), or for what uses the folder to end.</summary>
    public const string Waiting = "waiting";

    /// <summary>Finding out how much there is (walking the files).</summary>
    public const string Measuring = "measuring";

    public const string Writing = "writing";

    /// <summary>Writing an archive that is encrypted.</summary>
    public const string Encrypting = "encrypting";

    /// <summary>Reading the new archive back, to know it holds what was zipped.</summary>
    public const string Checking = "checking";

    /// <summary>Putting the new archive in place of the old one.</summary>
    public const string Replacing = "replacing";

    public const string Deleting = "deleting";

    /// <summary>Overwriting what was unzipped before it is deleted (encrypted ones).</summary>
    public const string Overwriting = "overwriting";

    /// <summary>Overwriting and deleting what zipping or unzipping left when the app stopped.</summary>
    public const string DeletingLeftovers = "deletingLeftovers";

    public const string Unzipping = "unzipping";

    /// <summary>Putting what was unzipped in its place.</summary>
    public const string Placing = "placing";

    /// <summary>Zipping the archived folders that go into a download as they are kept.</summary>
    public const string ZippingKept = "zippingKept";

    /// <summary>Copying an archive (the as-is download).</summary>
    public const string Copying = "copying";

    /// <summary>Copying, moving or deleting selected entries.</summary>
    public const string CopyingEntries = "copyingEntries";
    public const string MovingEntries = "movingEntries";
    public const string DeletingEntries = "deletingEntries";
}
