using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.HistoryViewing.Requests;
using notes.Features.HistoryViewing.States;
using notes.Pages.Editor;

namespace notes.Features.HistoryViewing.Changes;

/// <summary>The changes of a commit to the entry. Both states are disposed with the response.</summary>
/// <param name="Previous">The state before the commit, or null for the first commit.</param>
/// <param name="Before">The entry before the commit.</param>
/// <param name="After">The entry as the commit left it.</param>
/// <param name="Shown">What in the address says which commit it is, which every link of the page keeps (with <see cref="CommitChangesRequests.ChangesParameter"/>).</param>
public sealed record ShowChangesOfCommit(EarlierState State, EarlierState? Previous, string PhysicalPath, bool IsDirectory, ChangeSide Before, ChangeSide After,
    EarlierStateParameter Shown) : EarlierStateAnswer
{
    /// <summary>The state in which the entry is shown: the commit's, or the one before it for what it deleted (or what can only be seen then).</summary>
    public EarlierState StateShown => After.Kind == ChangeSideKind.Shown ? State : Previous!;
}

/// <summary>A redirect to the state of the commit (<c>?commit=</c> without the changes), with the reason (<see cref="EarlierStateNotice"/>).</summary>
public sealed record RedirectToStateOfCommit(string Notice) : EarlierStateAnswer;

/// <summary>
/// Answers <c>?commit=&amp;changes</c>, the changes of a commit to an entry, as docs/development/history-specification.md ("The changes of a
/// commit") says: to whoever may see a change of it in that commit.
/// </summary>
public sealed class CommitChangesRequests(EarlierStateRequests requests, EarlierStatePermissions permissions, CommitChanges changes)
{
    /// <summary>The query parameter that asks for the changes of the commit (<c>?commit=</c>) instead of its state.</summary>
    public const string ChangesParameter = "changes";

    /// <summary>With <c>?raw</c> in the changes of a commit: the bytes as they were before it, instead of as it left them.</summary>
    public const string PreviousParameter = "previous";

    public EarlierStateAnswer AnswerRequestForChanges(ClaimsPrincipal user, string virtualPath, string commitText)
    {
        if (requests.OpenAskedState(user, virtualPath, beforeText: null, commitText, out AskedState? opened) is { } failure)
            return failure;

        (EarlierState state, EarlierStateParameter asked, string? existingPath, string typedPath) = opened!;
        EarlierState? previous = state.OpenPrevious();
        // An entry that is gone now has its path as the history spells it: as the commit left it, or before it for what it deleted
        string gitPath = state.FindEntry(typedPath)?.GitPath ?? previous?.FindEntry(typedPath)?.GitPath ?? typedPath;
        string physicalPath = existingPath ?? state.ToPhysicalPath(gitPath);

        EarlierStateAnswer? notShown = null;
        ChangeSide before = ChangeSide.NotThere, after = ChangeSide.NotThere;
        // Now first: who may not see the history of the entry now learns nothing about it at that time
        if (!permissions.MaySeeHistoryOfEntryNow(user, physicalPath))
            notShown = requests.AnswerForEntryThatCantBeShown(user, existingPath, EarlierStateVerdict.NotPermitted);
        else
        {
            before = changes.SideOf(user, physicalPath, gitPath, previous);
            after = changes.SideOf(user, physicalPath, gitPath, state);
            // It has to be open on one side; why it isn't is told as for the state of the commit
            if (before.Kind != ChangeSideKind.Shown && after.Kind != ChangeSideKind.Shown)
                notShown = requests.AnswerForEntryThatCantBeShown(user, existingPath, permissions.CheckWhatDependsOnTheState(user, physicalPath, gitPath, state));
            else if (!changes.ChangedVisibly(user, gitPath, state, previous))
                notShown = new RedirectToStateOfCommit(EarlierStateNotice.NotChangedByCommit);
        }

        if (notShown is not null)
        {
            previous?.Dispose();
            state.Dispose();
            return notShown;
        }

        bool isDirectory = (after.Entry ?? before.Entry)!.IsDirectory;
        return new ShowChangesOfCommit(state, previous, physicalPath, isDirectory, before, after, asked);
    }

    /// <summary><c>?raw</c> of the changes of a commit: the bytes of the file as the commit left them, or before it; 404 where that side isn't shown.</summary>
    public static IActionResult RawBytesOfSide(HttpResponse response, ShowChangesOfCommit shown, bool previous)
        => (previous ? shown.Before : shown.After).Entry is { Blob: { } blob } entry
            ? RawHandler.HandleContents(response, entry.Name, EarlierStateRequests.ReadAllBytes(blob))
            : new NotFoundResult();
}
