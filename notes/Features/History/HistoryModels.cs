using System.Security.Claims;
using notes.Features.Auth;

namespace notes.Features.History;

/// <summary>Who a commit is by: the user who made the changes in it, or the app for changes it can't tell the user of.</summary>
public sealed record HistoryAuthor(string Name, string Email)
{
    /// <summary>For changes made outside of the app, or before it last started.</summary>
    public static HistoryAuthor App { get; } = new("_", "_");

    public static HistoryAuthor Of(ClaimsPrincipal user)
    {
        string? id = user.FindFirst(NotesClaims.UserId)?.Value;
        string name = Clean(user.FindFirst(NotesClaims.UserName)?.Value ?? id);
        // Git needs an email, but not every user has one: the id tells them apart as well
        string email = Clean(user.FindFirst(NotesClaims.Email)?.Value ?? id);
        return name == "" ? App : new HistoryAuthor(name, email == "" ? name : email);
    }

    /// <summary>Git doesn't allow angle brackets in names and emails (they delimit the email in a commit).</summary>
    private static string Clean(string? text)
        => new string((text ?? "").Where(character => character is not ('<' or '>') && !char.IsControl(character)).ToArray()).Trim();
}

/// <summary>What an operation did, which decides what the history does right after it.</summary>
public enum HistoryChangeKind
{
    /// <summary>The contents of a file were saved. Whether to commit was decided before (see <see cref="HistoryService.BeforeChange"/>).</summary>
    Save,

    /// <summary>Entries were created, uploaded, renamed, copied or moved.</summary>
    Change,

    /// <summary>Entries were deleted: a binary file among them that was committed is committed as deleted right away.</summary>
    Delete
}

/// <summary>Why a commit was made, which is its message (see <see cref="CommitKeywords"/>).</summary>
public enum CommitReason
{
    Idle,
    Threshold,
    OtherUser,
    BinaryDeleted,
    Startup,
    Migration,

    /// <summary>A configuration is about to change what the history keeps: what was pending until then.</summary>
    BeforeGitignore,

    /// <summary>A configuration changed what the history keeps: the configuration, and the files that leave or come back with it.</summary>
    Gitignore
}

/// <summary>
/// The messages of the commits: a keyword for why it was made, which a page that shows the history turns into text. The keywords are stored
/// in the history, so they never change once released. They start with <c>#</c>, so that text can follow them one day and still be told apart.
/// </summary>
public static class CommitKeywords
{
    /// <summary>The empty commit the history begins with.</summary>
    public const string Init = "#init";

    public const string Idle = "#idle";
    public const string Threshold = "#threshold";
    public const string OtherUser = "#other-user";
    public const string BinaryDeleted = "#binary-deleted";
    public const string Startup = "#startup";
    public const string Migration = "#migration";
    public const string BeforeGitignore = "#before-gitignore";
    public const string Gitignore = "#gitignore";

    public static string Of(CommitReason reason)
        => reason switch
        {
            CommitReason.Idle => Idle,
            CommitReason.Threshold => Threshold,
            CommitReason.OtherUser => OtherUser,
            CommitReason.BinaryDeleted => BinaryDeleted,
            CommitReason.Startup => Startup,
            CommitReason.Migration => Migration,
            CommitReason.BeforeGitignore => BeforeGitignore,
            CommitReason.Gitignore => Gitignore,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
}
