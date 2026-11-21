using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Notes.Features.Components.Download;
using Notes.Features.Components.Selection;
using Notes.Features.Configuration;
using Notes.Features.HistoryViewing.Changes;
using Notes.Features.HistoryViewing.Commits;
using Notes.Features.HistoryViewing.Views;
using Notes.Features.Media;
using Notes.Pages.Browse;
using Notes.Pages.Editor;

namespace Notes.Features.HistoryViewing.Requests;

/// <summary>
/// What the page of an entry shows of an earlier state (<see cref="HistoryRequests"/>): the card of the state, and the entry as it was or what
/// the commit changed of it (one of <see cref="Directory"/>, <see cref="File"/> and <see cref="FileChanges"/>).
/// </summary>
public sealed record EarlierStatePage(_EarlierStateCard Card, DirectoryView? Directory = null, FileView? File = null,
    _FileChanges? FileChanges = null);

/// <summary>The answer to a request about the history: an answer of its own, or a page (<see cref="EarlierStatePage"/>).</summary>
public sealed record HistoryAnswer(IActionResult? Result, EarlierStatePage? Page = null);

/// <summary>
/// Answers the GET requests of the page of an entry that are about its history: the entry as it was (<c>?before=</c>, <c>?commit=</c>), what
/// a commit changed of it (<c>&amp;changes</c>), the commits that changed it (<c>?commits</c>) and the times at which it can be seen
/// (<c>?history</c>). They have rules of their own (docs/development/history-specification.md), also for paths where nothing is now, so they
/// come before the page looks for the entry on disk.
/// </summary>
public sealed class HistoryRequests(
    EarlierStateRequests earlierStateRequests,
    EarlierStateViews earlierStateViews,
    CommitChangesRequests commitChangesRequests,
    CommitChangesViews commitChangesViews,
    ThumbnailHandler thumbnailHandler,
    IModelMetadataProvider modelMetadata)
{
    /// <summary>Whether the request is about the history, and so for <see cref="AnswerAsync"/>.</summary>
    public static bool IsFor(IQueryCollection query)
        => query.ContainsKey(_CommitSidebar.CommitListParameter)
           || query.ContainsKey(CommitChangesRequests.ChangesParameter)
           || EarlierStateParameter.IsIn(query)
           || query.ContainsKey(_HistoryModal.VisibleTimesParameter);

    /// <param name="virtualPath">The path of the entry as the address has it, without slashes at the start and end.</param>
    public async Task<HistoryAnswer> AnswerAsync(HttpContext context, string virtualPath)
    {
        IQueryCollection query = context.Request.Query;

        // The list of changes is asked for with the state that is shown, so it goes first
        if (query.ContainsKey(_CommitSidebar.CommitListParameter))
            return new(CommitsThatChangedEntry(context, virtualPath));
        // The changes are those of a commit: without one they are of nothing
        if (query.ContainsKey(CommitChangesRequests.ChangesParameter) && !query.ContainsKey(EarlierStateParameter.Commit))
            return new(new BadRequestResult());
        if (EarlierStateParameter.IsIn(query))
            return await EarlierStateAsync(context, virtualPath);
        return new(TimesAtWhichEntryCanBeSeen(context.User, virtualPath));
    }

    /// <summary>An address with <c>?before=</c> or <c>?commit=</c>: the entry as it was, if the user may see it so.</summary>
    private async Task<HistoryAnswer> EarlierStateAsync(HttpContext context, string virtualPath)
    {
        IQueryCollection query = context.Request.Query;
        // They are about the entries as they are now; and a page shows one state, not two
        if (query.ContainsKey(ConfigHandler.ConfigParameter) || query.ContainsKey(SelectionHandler.SelectionParameter)
            || (query.ContainsKey(EarlierStateParameter.Before) && query.ContainsKey(EarlierStateParameter.Commit)))
            return new(new BadRequestResult());
        // At most one of what can be read of a state, as for the entry as it is now
        if (new[] { RawHandler.RawParameter, ThumbnailHandler.ThumbnailParameter, DownloadHandler.DownloadParameter }.Count(query.ContainsKey) > 1)
            return new(new BadRequestResult());

        // The changes of a commit (they are no state that could be downloaded)
        string? commit = query[EarlierStateParameter.Commit];
        bool showsChanges = query.ContainsKey(CommitChangesRequests.ChangesParameter);
        if (showsChanges && query.ContainsKey(DownloadHandler.DownloadParameter))
            return new(new BadRequestResult());

        EarlierStateAnswer answer = showsChanges
            ? commitChangesRequests.AnswerRequestForChanges(context.User, virtualPath, commit!)
            : earlierStateRequests.AnswerRequestForEarlierState(context.User, virtualPath, query[EarlierStateParameter.Before], commit);
        switch (answer)
        {
            case AnswerWithStatus status:
                return new(new StatusCodeResult(status.StatusCode));
            case RedirectToNow redirect:
                EarlierStateNotice.Set(context, redirect.Notice);
                return new(new RedirectResult(AddressOfNow(context.Request)));
            case RedirectToStateOfCommit redirect:
                EarlierStateNotice.Set(context, redirect.Notice);
                string now = AddressOfNow(context.Request);
                return new(new RedirectResult(now + (now.Contains('?') ? "&" : "?")
                    + EarlierStateParameter.ForCommit(commit!.ToLowerInvariant()).QueryPart));
            case ShowEntryAsItWas shown:
                // The state is read from until the response is sent (a zip is written while it is sent)
                context.Response.RegisterForDispose(shown.State);
                return await EntryAsItWasAsync(context, virtualPath, shown);
            case ShowChangesOfCommit changes:
                context.Response.RegisterForDispose(changes.State);
                if (changes.Previous is not null)
                    context.Response.RegisterForDispose(changes.Previous);
                return await ChangesOfCommitAsync(context, virtualPath, changes);
            default:
                throw new InvalidOperationException("Unknown answer to a request for an earlier state.");
        }
    }

    private async Task<HistoryAnswer> ChangesOfCommitAsync(HttpContext context, string virtualPath, ShowChangesOfCommit shown)
    {
        IQueryCollection query = context.Request.Query;
        bool previous = query.ContainsKey(CommitChangesRequests.PreviousParameter);

        // The bytes of a side of the change, for the browser to show it
        if (query.ContainsKey(RawHandler.RawParameter))
            return new(commitChangesRequests.RawBytesOfSide(context.Response, shown, previous));
        if (query.ContainsKey(ThumbnailHandler.ThumbnailParameter))
            return new((previous ? shown.Before : shown.After).Entry is { Blob: { } sideBlob } side
                ? await thumbnailHandler.HandleContentsAsync(context.Response, side.Name, sideBlob.Size, ()
                    => EarlierStateRequests.ReadAllBytes(sideBlob))
                : new NotFoundResult());

        string addressOfNow = AddressOfNow(context.Request);
        _EarlierStateCard card = earlierStateViews.CreateCard(context.User, shown.State, shown.Shown, addressOfNow, showsChanges: true);
        return new(null, shown.IsDirectory
            ? new EarlierStatePage(card, Directory: commitChangesViews.CreateDirectoryView(context.User, virtualPath, shown, addressOfNow))
            : new EarlierStatePage(card, FileChanges: await commitChangesViews.CreateFileChangesAsync(context.User, virtualPath, shown,
                addressOfNow)));
    }

    private async Task<HistoryAnswer> EntryAsItWasAsync(HttpContext context, string virtualPath, ShowEntryAsItWas shown)
    {
        IQueryCollection query = context.Request.Query;

        // The bytes and the download of the entry as it was, like those of the entry as it is
        if (query.ContainsKey(RawHandler.RawParameter))
            return new(earlierStateRequests.RawBytesAsTheyWere(context.Response, shown));
        if (query.ContainsKey(ThumbnailHandler.ThumbnailParameter))
            return new(shown.Entry.Blob is { } blob
                ? await thumbnailHandler.HandleContentsAsync(context.Response, shown.Entry.Name, blob.Size, ()
                    => EarlierStateRequests.ReadAllBytes(blob))
                : new NotFoundResult());
        if (query.ContainsKey(DownloadHandler.DownloadParameter))
            return new(earlierStateRequests.DownloadAsItWas(context.User, virtualPath, shown));

        // Otherwise its page, in which nothing can be changed (?edit shows the source of a markdown file, read-only)
        string addressOfNow = AddressOfNow(context.Request);
        _EarlierStateCard card = earlierStateViews.CreateCard(context.User, shown.State, shown.Shown, addressOfNow);
        return new(null, shown.Entry.IsDirectory
            ? new EarlierStatePage(card, Directory: earlierStateViews.CreateDirectoryView(context.User, virtualPath, shown, addressOfNow))
            : new EarlierStatePage(card, File: await earlierStateViews.CreateFileViewAsync(context.User, virtualPath, shown, addressOfNow,
                query.ContainsKey("edit"), context.Request.Host.Host)));
    }

    /// <summary><c>?history</c>: the times at which the entry can be seen as it was, for the dialog of the history button.</summary>
    private IActionResult TimesAtWhichEntryCanBeSeen(ClaimsPrincipal user, string virtualPath)
    {
        if (earlierStateRequests.TimesAtWhichEntryCanBeSeen(user, virtualPath) is not { } ranges)
            return new NotFoundResult();

        return new JsonResult(new
        {
            ranges = ranges.Select(range => new { after = EarlierStateTime.Format(range.After), until = range.Until is { } until
                ? EarlierStateTime.Format(until) : null })
        });
    }

    /// <summary>
    /// <c>?commits</c>: a page (<c>&amp;page=</c>, from 1) of the commits that changed the entry, for the sidebar of the list of changes (as its
    /// HTML), from the state that is shown (the <c>?before=</c> or <c>?commit=</c> it is asked with) back; with <c>&amp;newer</c>, the lines of
    /// those right after that state instead.
    /// </summary>
    private IActionResult CommitsThatChangedEntry(HttpContext context, string virtualPath)
    {
        IQueryCollection query = context.Request.Query;
        int page = int.TryParse(query[_CommitSidebar.PageParameter], out int asked) && asked >= 1 ? asked : 1;
        string? commit = query[EarlierStateParameter.Commit].FirstOrDefault();
        string? before = query[EarlierStateParameter.Before].FirstOrDefault();
        // The page asks with the state it shows, which is a valid one (otherwise it would have been sent to now)
        if ((commit is not null && !EarlierStateParameter.IsCommitId(commit)) || (commit is null && before is not null
            && !EarlierStateTime.TryParse(before, out _)))
            return new BadRequestResult();
        EarlierStateParameter? shown = commit is not null
            ? EarlierStateParameter.ForCommit(commit.ToLowerInvariant())
            : before is not null ? new EarlierStateParameter(EarlierStateParameter.Before, before) : null;
        bool newer = query.ContainsKey(_CommitSidebar.NewerParameter);
        if (earlierStateRequests.CommitsThatChangedEntry(context.User, virtualPath, shown, page, newer) is not { } found)
            return new NotFoundResult();

        string? shownCommitId = shown is { IsCommit: true } ? shown.Value : null;
        return newer
            ? Partial("/Features/HistoryViewing/Commits/_CommitLines.cshtml", _CommitLines.Of(found, shownCommitId))
            : Partial("/Features/HistoryViewing/Commits/_CommitList.cshtml", _CommitList.Of(found, shownCommitId));
    }

    /// <summary>
    /// The partial with its viewmodel as the whole answer, as <see cref="Microsoft.AspNetCore.Mvc.RazorPages.PageModel.Partial"/> makes it.
    /// </summary>
    private PartialViewResult Partial(string viewName, object model)
        => new() { ViewName = viewName, ViewData = new ViewDataDictionary(modelMetadata, new ModelStateDictionary()) { Model = model } };

    /// <summary>
    /// The address of the page without <c>?before=</c>, <c>?commit=</c> and what goes with a commit (<c>&amp;changes</c>, <c>&amp;previous</c>;
    /// the rest of the query as it is): the entry as it is now.
    /// </summary>
    private static string AddressOfNow(HttpRequest request)
    {
        string[] stateParameters = [EarlierStateParameter.Before, EarlierStateParameter.Commit, CommitChangesRequests.ChangesParameter,
            CommitChangesRequests.PreviousParameter];
        // The other parts of the query are kept as they were written (not encoded again), so that only the state goes
        string[] kept = [.. (request.QueryString.Value ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Split('=')[0] is var name && !stateParameters.Contains(name, StringComparer.OrdinalIgnoreCase))];
        return request.PathBase.ToUriComponent() + request.Path.ToUriComponent() + (kept.Length > 0 ? "?" + string.Join('&', kept) : "");
    }
}
