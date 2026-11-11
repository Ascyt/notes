using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using notes.Features.Auth;
using notes.Features.Components.Download;
using notes.Features.Helpers;
using notes.Features.History;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.States;
using notes.Pages.Editor;

namespace notes.Features.HistoryViewing.Requests;

/// <summary>What a request for an earlier state gets.</summary>
public abstract record EarlierStateAnswer;

/// <summary>The entry as it was. The state is disposed with the response.</summary>
/// <param name="Shown">What in the address says which state it is, which every link of the page keeps.</param>
public sealed record ShowEntryAsItWas(EarlierState State, EarlierEntry Entry, string PhysicalPath, EarlierStateParameter Shown) : EarlierStateAnswer;

/// <summary>A redirect to the entry as it is now, with the reason (<see cref="EarlierStateNotice"/>).</summary>
public sealed record RedirectToNow(string Notice) : EarlierStateAnswer;

/// <summary>An error status: 404 like a missing path, 403 like an unreadable entry.</summary>
public sealed record AnswerWithStatus(int StatusCode) : EarlierStateAnswer;

/// <summary>
/// Answers the requests for earlier states of the notes (<c>?before=</c> and <c>?commit=</c>, <c>?history</c> for the times at which an entry
/// can be seen, <c>?commits</c> for the list of its changes), as docs/development/history-specification.md says. Who may see what is decided by
/// <see cref="EarlierStatePermissions"/>.
/// </summary>
public sealed class EarlierStateRequests(Config config, IPathService path, AccessTree tree, EarlierStatePermissions permissions, EarlierStateReader reader,
    EarlierStateCommits commits, HistoryService history, DownloadHandler downloadHandler, TimeProvider time)
{
    /// <summary>
    /// What <c>?before=</c> (<paramref name="beforeText"/>) or <c>?commit=</c> (<paramref name="commitText"/>, which goes first) gets for the
    /// address (<paramref name="virtualPath"/>, as typed).
    /// </summary>
    public EarlierStateAnswer AnswerRequestForEarlierState(ClaimsPrincipal user, string virtualPath, string? beforeText, string? commitText)
    {
        // Which state is asked for, if it can be one at all
        EarlierStateParameter asked;
        if (commitText is not null)
        {
            if (!EarlierStateParameter.IsCommitId(commitText))
                return new RedirectToNow(EarlierStateNotice.InvalidCommit);
            asked = EarlierStateParameter.ForCommit(commitText.ToLowerInvariant());
        }
        else
        {
            if (!EarlierStateTime.TryParse(beforeText, out DateTimeOffset before))
                return new RedirectToNow(EarlierStateNotice.InvalidTime);
            if (before > time.GetUtcNow())
                return new RedirectToNow(EarlierStateNotice.FutureTime);
            asked = EarlierStateParameter.ForTime(before);
        }

        // Whether there is a history is how the site is run: only admins are told
        if (!reader.HasHistory)
            return new RedirectToNow(tree.CanManageConfiguration(user) ? EarlierStateNotice.NoHistory : EarlierStateNotice.NotPermitted);

        // What is there now is answered as it is now first: a hidden entry like a missing one, an unreadable one as unreadable
        string? existingPath = path.ToPhysicalPath(config.Dir, virtualPath);
        if (existingPath is not null)
        {
            if (!tree.IsKnown(user, existingPath))
                return new AnswerWithStatus(StatusCodes.Status404NotFound);
            if (!tree.GetAccess(user, existingPath).CanRead)
                // The root page is there for everyone (it has the way to log in), so it is gone back to
                return virtualPath == "" ? new RedirectToNow(EarlierStateNotice.NotPermitted) : new AnswerWithStatus(StatusCodes.Status403Forbidden);
        }
        else if (PhysicalPathAsTyped(virtualPath) is null)
            return new AnswerWithStatus(StatusCodes.Status404NotFound);

        EarlierState? state = OpenState(asked);
        if (state is null)
            return existingPath is null ? new AnswerWithStatus(StatusCodes.Status404NotFound)
                // Before the first state there was nothing; a commit that isn't there is about the commit, not the entry
                : new RedirectToNow(asked.IsCommit ? EarlierStateNotice.NoSuchCommit : EarlierStateNotice.DidNotExistThen);

        // An entry that is gone now has its path as the history spells it (which may differ in case from the address)
        string gitPath = GitPathAsTyped(virtualPath);
        string physicalPath = existingPath ?? state.ToPhysicalPath(state.FindEntry(gitPath)?.GitPath ?? gitPath);

        EarlierStateVerdict verdict = permissions.CheckWhetherEntryCanBeSeenAsItWas(user, physicalPath, state);
        if (verdict == EarlierStateVerdict.CanBeSeen)
            return new ShowEntryAsItWas(state, state.FindEntry(state.ToGitPath(physicalPath))!, physicalPath, asked);

        state.Dispose();
        // Only what is there now (and may be opened) can be gone back to; anything else is answered like a missing path
        if (existingPath is null)
            return new AnswerWithStatus(StatusCodes.Status404NotFound);
        return new RedirectToNow(verdict switch
        {
            // Only those who manage the configuration may know what it leaves out
            EarlierStateVerdict.NotKeptInHistory => tree.CanManageConfiguration(user) ? EarlierStateNotice.NotKeptInHistory : EarlierStateNotice.NotPermitted,
            EarlierStateVerdict.DidNotExistThen => EarlierStateNotice.DidNotExistThen,
            _ => EarlierStateNotice.NotPermitted
        });
    }

    /// <summary>
    /// The times at which the entry can be seen as it was (<c>?history</c>), or null where there is nothing to say: the user may not see its
    /// history now, it is hidden, or the history doesn't keep it. Those are answered like a missing path.
    /// </summary>
    public IReadOnlyList<VisibleTimeRange>? TimesAtWhichEntryCanBeSeen(ClaimsPrincipal user, string virtualPath)
        => EntryWhoseHistoryMayBeSeen(user, virtualPath) is { } physicalPath ? permissions.TimesAtWhichEntryCanBeSeen(user, physicalPath) : null;

    /// <summary>
    /// The commits that changed the entry (<c>?commits</c>): the <paramref name="page"/>-th page of them from the state that is shown
    /// (<paramref name="shown"/>, null for now) back, or with <paramref name="newer"/> those that come right after it. Null where there is
    /// nothing to say, as for <see cref="TimesAtWhichEntryCanBeSeen"/>.
    /// </summary>
    public CommitList? CommitsThatChangedEntry(ClaimsPrincipal user, string virtualPath, EarlierStateParameter? shown, int page, bool newer)
    {
        if (EntryWhoseHistoryMayBeSeen(user, virtualPath) is not { } physicalPath)
            return null;

        CommitList none = new([], page, HasOlder: false, NewerAfter: null);
        // Now is the newest state: there is nothing after it
        if (shown is null)
            return newer ? none : commits.PageOfChanges(user, physicalPath, null, page);

        string commitId;
        using (EarlierState? state = OpenState(shown))
        {
            if (state is null)
                return none;
            commitId = state.CommitId;
        }

        return newer ? commits.NewerChanges(user, physicalPath, commitId) : commits.PageOfChanges(user, physicalPath, commitId, page);
    }

    /// <summary>
    /// The physical path of the entry, if the user may see its history now and the history keeps it; otherwise null, also for what is hidden.
    /// It may be an entry that isn't there any more.
    /// </summary>
    private string? EntryWhoseHistoryMayBeSeen(ClaimsPrincipal user, string virtualPath)
    {
        if (!reader.HasHistory)
            return null;

        string? existingPath = path.ToPhysicalPath(config.Dir, virtualPath);
        if (existingPath is not null && (!tree.IsKnown(user, existingPath) || !history.IsKept(existingPath, Directory.Exists(existingPath))))
            return null;

        string? physicalPath = existingPath ?? PhysicalPathAsTyped(virtualPath);
        return physicalPath is not null && permissions.MaySeeHistoryOfEntryNow(user, physicalPath) ? physicalPath : null;
    }

    /// <summary>The state the address asks for, if the history has it. To be disposed.</summary>
    private EarlierState? OpenState(EarlierStateParameter asked)
    {
        if (asked.IsCommit)
            return reader.OpenStateOfCommit(asked.Value);
        return EarlierStateTime.TryParse(asked.Value, out DateTimeOffset before) ? reader.OpenStateBefore(before) : null;
    }

    /// <summary><c>?raw</c> of an earlier state: the bytes of a media file as they were.</summary>
    public static IActionResult RawBytesAsTheyWere(HttpResponse response, ShowEntryAsItWas shown)
        => shown.Entry.Blob is { } blob
            // Read into memory, so that a video can be played from anywhere (which needs a stream that can seek)
            ? RawHandler.HandleContents(response, shown.Entry.Name, ReadAllBytes(blob))
            : new NotFoundResult();

    /// <summary><c>?download</c> of an earlier state: a file as it was, a directory as a zip of what may be seen of it as it was.</summary>
    public IActionResult DownloadAsItWas(ClaimsPrincipal user, string virtualPath, ShowEntryAsItWas shown)
    {
        if (shown.Entry.Blob is { } blob)
            return new FileStreamResult(blob.GetContentStream(), "application/octet-stream") { FileDownloadName = shown.Entry.Name };

        return downloadHandler.ZipDownload(ZipFolderAsItWas(user, shown.Entry, shown.State), virtualPath);
    }

    /// <summary>The directory as it was, for a zip: what of it may be seen as it was, each file with the time of the state.</summary>
    private ZipFolderEntry ZipFolderAsItWas(ClaimsPrincipal user, EarlierEntry directory, EarlierState state)
        => new(directory.Name, () => permissions.ListDirectoryAsItWas(user, directory, state)
            .Where(listed => listed.Kind == EarlierListingKind.Link)
            .Select(listed => listed.Entry.Blob is { } blob
                ? new ZipFileEntry(listed.Entry.Name, state.CommitTime, () => blob.GetContentStream())
                : (ZipEntry)ZipFolderAsItWas(user, listed.Entry, state)));

    /// <summary>The physical path the address names, for an entry that isn't there now: null if one of its names can't be a name at all.</summary>
    private string? PhysicalPathAsTyped(string virtualPath)
    {
        string[] names = NamesOf(virtualPath);
        return names.All(PathService.IsValid) ? Path.Combine([Path.GetFullPath(config.Dir), .. names]) : null;
    }

    private static string GitPathAsTyped(string virtualPath)
        => string.Join('/', NamesOf(virtualPath));

    private static string[] NamesOf(string virtualPath)
        => virtualPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static byte[] ReadAllBytes(LibGit2Sharp.Blob blob)
    {
        using Stream contents = blob.GetContentStream();
        using MemoryStream copy = new();
        contents.CopyTo(copy);
        return copy.ToArray();
    }
}
