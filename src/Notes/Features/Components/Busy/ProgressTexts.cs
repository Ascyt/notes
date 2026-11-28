namespace Notes.Features.Components.Busy;

/// <summary>
/// How the text under a bar says what long work is doing (<see cref="Helpers.WorkStatus"/>, <c>describeProgress</c> in site.ts), on every
/// page. A site with texts of its own derives from this class and registers it in place of this one, as for <see cref="BusyTexts"/>.
/// </summary>
public class ProgressTexts
{
    // What it is doing (one for each of Helpers.WorkPhases)
    public virtual string Waiting => "Waiting for its turn";
    public virtual string Measuring => "Counting what there is";
    public virtual string Writing => "Writing the archive";
    public virtual string Encrypting => "Writing and encrypting the archive";
    public virtual string Checking => "Checking the new archive";
    public virtual string Replacing => "Putting the new archive in place";
    public virtual string Deleting => "Deleting the unzipped copy";
    public virtual string Overwriting => "Overwriting and deleting the unzipped copy";
    public virtual string Unzipping => "Unzipping";
    public virtual string Placing => "Putting what was unzipped in place";
    public virtual string ZippingKept => "Zipping the archived directories that go in as they are kept";
    public virtual string Copying => "Copying the archive";
    public virtual string Uploading => "Uploading";

    public virtual string CopyingEntries => "Copying";
    public virtual string MovingEntries => "Moving";
    public virtual string DeletingEntries => "Deleting";

    /// <summary>The hourglass of what waits (in place of its bar), and the tooltip that says for what, where that is known.</summary>
    public virtual string Pending => "Pending: it waits until it may begin.";

    public virtual string PendingFor => "Pending: it waits for {operation}.";
    public virtual string WaitingFor => "Waiting for {operation}";

    // How much of it: "Writing the archive: 12 of 340 files, 1.2 of 40.0 MiB (35%)"
    public virtual string Files => "{done} of {total} files";

    /// <summary>What an archive holds, directories included (unzipping counts them too).</summary>
    public virtual string Entries => "{done} of {total} entries";

    public virtual string Bytes => "{done} of {total}";
    public virtual string Percent => "{percent}%";
    public virtual string WithCounts => "{phase}: {counts}";
    public virtual string WithPercent => "{text} ({percent})";
}
