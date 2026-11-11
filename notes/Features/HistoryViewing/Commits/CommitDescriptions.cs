using notes.Features.History;

namespace notes.Features.HistoryViewing.Commits;

/// <summary>
/// The text the list of changes shows for the message of a commit, which is a keyword for why it was made (<see cref="CommitKeywords"/>). Why a
/// configuration was changed or brought up to date is only told to those who manage the configuration (see the history specification).
/// </summary>
public static class CommitDescriptions
{
    /// <summary>What is said instead where the reason is about the configuration, and for messages this version doesn't know.</summary>
    public const string Changes = "Changes";

    public static string Describe(string message, bool managesConfiguration)
        => message switch
        {
            CommitKeywords.Init => "History started",
            CommitKeywords.Idle => "After a pause",
            CommitKeywords.Threshold => "Many lines changed",
            CommitKeywords.OtherUser => "Before a change by another user",
            CommitKeywords.BinaryDeleted => "Binary file deleted",
            CommitKeywords.Startup => "Changed while the app was not running",
            CommitKeywords.Migration when managesConfiguration => "Configuration brought up to date",
            CommitKeywords.BeforeGitignore when managesConfiguration => "Before a change of what the history keeps",
            CommitKeywords.Gitignore when managesConfiguration => "What the history keeps changed",
            _ => Changes
        };
}
