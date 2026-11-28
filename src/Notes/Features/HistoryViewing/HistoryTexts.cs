using Notes.Features.Components.Assets;

namespace Notes.Features.HistoryViewing;

/// <summary>
/// The texts of seeing the history: the choice of a time (<see cref="Views._HistoryModal"/>), the card of the state that is shown
/// (<see cref="Views._EarlierStateCard"/>), the list of changes (<see cref="Commits._CommitSidebar"/>), the changes of a commit to a file
/// (<see cref="Changes._FileChanges"/>, <see cref="Changes._ChangeMark"/>), and what their scripts say. A site with texts of its own derives
/// from this class and registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>. The scripts' texts are
/// properties, with what changes in braces.
/// </summary>
public class HistoryTexts
{
    public virtual string BackToNow => "Back to now";
    public virtual string Close => "Close";
    public virtual string Cancel => "Cancel";

    /// <summary>Who made a commit that no user made in the app (said in italics, as it is no name).</summary>
    public virtual string Automatic => "automatic";

    // The choice of a time
    public virtual string ViewEarlierState => "View an earlier state";
    public virtual string ShownAsItWasBefore => "Shown as it was before";
    public virtual string WhatIsShown(bool isFile)
        => $"The current {(isFile ? "file" : "directory")} and everything reached from it are shown as they were at that time, read-only.";
    public virtual string CanBeSeenAsItWas => "Can be seen as it was:";
    public virtual string View => "View";
    public virtual string ViewThisState => "View this state";
    public virtual string ChooseATime => "Choose a time first.";
    public virtual string NotATime => "That is not a valid time.";
    public virtual string InTheFuture => "The time is in the future.";
    public virtual string FileCantBeSeenThen => "The file can't be seen as it was at that time.";
    public virtual string DirectoryCantBeSeenThen => "The directory can't be seen as it was at that time.";
    public virtual string SeenBetween => "After {after}, up to {until}";
    public virtual string Now => "now";
    public virtual string AtNoTime => "At no time.";
    public virtual string Loading => "Loading…";
    public virtual string TimesNotLoaded => "The times could not be loaded.";

    // The card of the state that is shown
    public virtual string ChangesMadeOn => "Changes made on";
    public virtual string ShownAsItWasOn => "Shown as it was on";

    // The list of changes
    public virtual string ChangesOf(bool isFile) => isFile ? "Changes of this file" : "Changes of this directory";
    public virtual string HideTheChanges => "Hide the changes";
    public virtual string Changes => "Changes";
    public virtual string None => "None";
    public virtual string Newer => "Newer";
    public virtual string Older => "Older";
    public virtual string NewerChanges => "Newer changes";
    public virtual string OlderChanges => "Older changes";
    public virtual string NewestAlready => "These are the newest changes.";
    public virtual string NoOlder => "There are no older changes.";
    public virtual string Page(int page) => $"Page {page}";
    public virtual string ShowNewer => "Show newer…";
    public virtual string ShowNewerChanges => "Show newer changes";
    public virtual string ShowWhatCommitChanged => "Show what this commit changed";
    public virtual string ChangesNotLoaded => "The changes could not be loaded.";
    public virtual string NewerChangesNotLoaded => "The newer changes could not be loaded.";

    // The changes of a commit to a file
    public virtual string BeforeCommit => "Before this commit";
    public virtual string AfterCommit => "After this commit";
    public virtual string NotThere => "Not there.";
    public virtual string CantBeSeen => "Insufficient permissions to see the file as it was.";
    public virtual string TextOtherSideBinary => "Text (the other side is binary).";
    public virtual string BinaryCantBeShown => "Binary, can't be shown here.";
    public virtual string AddedByCommit => "Added by this commit";
    public virtual string DeletedByCommit => "Deleted by this commit";
    public virtual string ChangedByCommit => "Changed by this commit";
    public virtual string CantBeSeenBefore => "The file can't be seen as it was before this commit.";
    public virtual string CantBeSeenAfter => "The file can't be seen as this commit left it.";

    // Why something can't be done or seen in an earlier state

    public virtual string CantBeChanged => "An earlier state can't be changed.";
    public virtual string NothingChangedByCommit(bool isFile) => $"Nothing of this {Kind(isFile)} was changed by this commit.";
    public virtual string NotThereInCommit(bool isFile) => $"This {Kind(isFile)} is not there in the state of this commit.";
    public virtual string CantBeSeenInCommit(bool isFile) => $"Insufficient permissions to see this {Kind(isFile)} as it was in the state of this commit.";
    public virtual string DirectoryLeftOut => "The configuration leaves this directory out of the history.";
    public virtual string DirectoryCantBeSeen => "Insufficient permissions to see this directory as it was.";

    /// <summary>Added to why a directory above can't be seen as it was, where it is gone now.</summary>
    public virtual string GoneNow => " It is not there any more.";

    // Why the history can't be seen here at all (for those who manage the configuration, who may know why)

    [NotForScripts]
    public virtual string BridgeHistoryNotViewable => "This is in a bridge (Config:Bridges), whose history is recorded but can't be viewed in the app yet.";
    [NotForScripts]
    public virtual string BridgeWithoutHistory => "This is in a bridge (Config:Bridges) without a history (History:Enabled is false).";
    [NotForScripts]
    public virtual string ArchiveHistoryNotViewable => "This is in an archived folder, whose history is recorded (in its archive) but can't be viewed in the app yet.";
    [NotForScripts]
    public virtual string ArchiveWithoutHistory => "This is in an archived folder inside a bridge without a history (History:Enabled is false).";
    public virtual string LeftOut(bool isFile) => $"The configuration leaves this {Kind(isFile)} out of the history (gitignore).";
    public virtual string TooBig(double maxMegabytes)
        => $"This file is larger than {maxMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture)} MB, so it is left out of the history (History:MaxFileMegabytes).";

    /// <summary>What a page that was led to says, as the address asked for an earlier state that can't be shown (<see cref="Requests.EarlierStateNotice"/>).</summary>
    public virtual string? Notice(string notice) => notice switch
    {
        Requests.EarlierStateNotice.InvalidTime => "The time of the earlier state is not a valid time.",
        Requests.EarlierStateNotice.FutureTime => "The time of the earlier state is in the future.",
        Requests.EarlierStateNotice.NoHistory => "There is no history.",
        Requests.EarlierStateNotice.NotPermitted => "Insufficient permissions to see this entry as it was at that time.",
        Requests.EarlierStateNotice.NotKeptInHistory => "The configuration leaves this entry out of the history.",
        Requests.EarlierStateNotice.DidNotExistThen => "This entry was not there at that time.",
        Requests.EarlierStateNotice.InvalidCommit => "The commit of the earlier state is not a valid commit id.",
        Requests.EarlierStateNotice.NoSuchCommit => "The history has no such commit.",
        Requests.EarlierStateNotice.NotChangedByCommit => "Nothing of this entry was changed by this commit.",
        _ => null
    };

    /// <summary>
    /// What the list of changes says of a commit, by the keyword of its message (<see cref="History.CommitKeywords"/>). Why a configuration
    /// was changed is only told to those who manage it (<paramref name="managesConfiguration"/>); everyone else, and messages this version
    /// doesn't know, get <see cref="Changes"/>.
    /// </summary>
    public virtual string CommitDescription(string message, bool managesConfiguration) => message switch
    {
        History.CommitKeywords.Init => "History started",
        History.CommitKeywords.Idle => "After a pause",
        History.CommitKeywords.Threshold => "Many lines changed",
        History.CommitKeywords.OtherUser => "Before a change by another user",
        History.CommitKeywords.BinaryDeleted => "Binary file deleted",
        History.CommitKeywords.Startup => "Changed while the app was not running",
        History.CommitKeywords.Zipping => "Before it was zipped",
        History.CommitKeywords.Migration when managesConfiguration => "Configuration brought up to date",
        History.CommitKeywords.BeforeGitignore when managesConfiguration => "Before a change of what the history keeps",
        History.CommitKeywords.Gitignore when managesConfiguration => "What the history keeps changed",
        _ => Changes
    };

    /// <summary>"file" or "directory", in the sentences above.</summary>
    protected virtual string Kind(bool isFile) => isFile ? "file" : "directory";
}
