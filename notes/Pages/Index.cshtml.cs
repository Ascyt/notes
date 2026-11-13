using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Auth;
using notes.Features.Components.Create;
using notes.Features.Components.Delete;
using notes.Features.Components.Download;
using notes.Features.Components.Rename;
using notes.Features.Components.Selection;
using notes.Features.Components.Transfer;
using notes.Features.Components.Upload;
using notes.Features.Configuration;
using notes.Features.Helpers;
using notes.Features.History;
using notes.Features.HistoryViewing.Changes;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Requests;
using notes.Features.HistoryViewing.Views;
using notes.Pages.Browse;
using notes.Pages.ConfigEditor;
using notes.Pages.Editor;

namespace notes.Pages;

/// <summary>
/// The page for every file and directory of the notes. It only finds what the address points at and passes the
/// request on to the feature that answers it (the query string says which one).
/// </summary>
/// <remarks>
/// There is no gate in front of it: anyone may ask for anything, and what they get is decided per entry. Whatever the request, the user must
/// be allowed to know of the entry it is for (<see cref="AccessTree.IsKnown"/>); one they aren't is answered exactly like one that doesn't exist.
/// Opening it (its page, its bytes, a download) also needs read access to it, otherwise 403.
/// Everything beyond that is up to the feature, see docs/development/security-specification.md.
/// </remarks>
public sealed class IndexModel(Config config,
    IPathService path,
    AccessTree tree,
    ConfigHandler configHandler,
    DownloadHandler downloadHandler,
    RawHandler rawHandler,
    CreateHandler createHandler,
    UploadHandler uploadHandler,
    RenameHandler renameHandler,
    SaveHandler saveHandler,
    DeleteHandler deleteHandler,
    SelectionHandler selectionHandler,
    TransferHandler transferHandler,
    ISelectionService selectionService,
    HistoryService history,
    EarlierStateRequests earlierStateRequests,
    EarlierStateViews earlierStateViews,
    CommitChangesRequests commitChangesRequests,
    CommitChangesViews commitChangesViews) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public DirectoryView? Directory { get; private set; }
    public new FileView? File { get; private set; }

    /// <summary>The changes of a commit to a file (<c>?commit=&amp;changes</c>), instead of the file.</summary>
    public _FileChanges? FileChanges { get; private set; }

    /// <summary>The editor of the configuration of the entry (<c>?config</c>), instead of the entry itself.</summary>
    public ConfigEditorView? ConfigEditor { get; private set; }

    /// <summary>The card of the earlier state that is shown (<c>?before=</c> or <c>?commit=</c>), or null for the notes as they are.</summary>
    public _EarlierStateCard? EarlierStateCard { get; private set; }

    /// <summary>Why the page was sent here from an earlier state (<see cref="EarlierStateNotice"/>), shown as an error message.</summary>
    public string? EarlierStateMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        // No body, so the error page is shown instead
        if (VirtualPath.StartsWith("_/"))
            return NotFound();

        // Earlier states have their own rules, also for paths that aren't there any more (docs/development/history-specification.md). The list
        // of changes is asked for with the state that is shown, so it goes first.
        if (Request.Query.ContainsKey(_CommitSidebar.CommitListParameter))
            return OnGetCommitsThatChangedEntry();
        // The changes are those of a commit: without one they are of nothing
        if (Request.Query.ContainsKey(CommitChangesRequests.ChangesParameter) && !Request.Query.ContainsKey(EarlierStateParameter.Commit))
            return BadRequest();
        if (EarlierStateParameter.IsIn(Request.Query))
            return await OnGetEarlierStateAsync();
        if (Request.Query.ContainsKey(_HistoryModal.VisibleTimesParameter))
            return OnGetTimesAtWhichEntryCanBeSeen();

        string? physicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        if (physicalPath is null || !System.IO.Path.Exists(physicalPath))
            return NotFound();

        PhysicalPath = physicalPath;

        if (!tree.IsKnown(User, PhysicalPath))
            return NotFound();

        // The configuration is for those who manage it. For everyone else there is no such thing, which is answered like a missing one.
        if (Request.Query.ContainsKey("config"))
        {
            if (!tree.CanManageConfiguration(User))
                return NotFound();

            ConfigEditor = configHandler.CreateView(VirtualPath, PhysicalPath);
            return Page();
        }

        if (Request.Query.ContainsKey("selection"))
            return selectionHandler.HandleGet(User);

        // The root page is the one everybody may open (it carries the login button); it shows nothing of the notes to those who may not read it.
        // Any other entry the user knows of but may not read (only logged-in users know of such) is listed in its directory, but can't be opened.
        EntryAccessInfo access = tree.GetAccess(User, PhysicalPath);
        bool canOpen = access.CanRead;
        bool isRoot = VirtualPath == "";
        if (!canOpen && !isRoot)
            return StatusCode(StatusCodes.Status403Forbidden);

        if (Request.Query.ContainsKey("download"))
            return canOpen ? downloadHandler.Handle(User, VirtualPath, PhysicalPath) : NotFound();

        if (Request.Query.ContainsKey("raw"))
            return canOpen ? rawHandler.Handle(Response, PhysicalPath) : NotFound();

        EarlierStateMessage = EarlierStateNotice.TakeMessage(HttpContext);
        bool isDirectory = System.IO.Directory.Exists(PhysicalPath);
        HistoryButtonState historyButton = DecideHistoryButtonState(access, isDirectory);

        if (isDirectory)
        {
            string? userId = SelectionHandler.UserIdOf(User);
            Directory = new DirectoryView(config, path, tree)
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath,
                Viewer = User,
                CanBrowse = canOpen,
                CanConfigure = tree.CanManageConfiguration(User),
                HistoryButton = historyButton,
                Selection = userId is null ? SelectionSnapshot.Empty : selectionService.Get(userId)
            };
            Directory.Init();
        }
        else if (System.IO.File.Exists(PhysicalPath))
        {
            TreeAccess inspected = tree.Inspect(User, PhysicalPath);
            File = new FileView
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath,
                CanWrite = access.CanWrite,
                CanConfigure = tree.CanManageConfiguration(User),
                HistoryButton = historyButton,
                EditRequested = Request.Query.ContainsKey("edit"),
                SiteHost = Request.Host.Host,
                DeleteDeniedReason = inspected.DeleteDeniedReason,
                RenameDeniedReason = tree.RenameDeniedReason(User, PhysicalPath, inspected)
            };
            await File.InitAsync();
        }

        return Page();
    }

    /// <summary>
    /// Whether the page has the way to the earlier states of the entry. Those who manage the configuration have it wherever there is a
    /// history, disabled with the reason where the configuration leaves the entry out of it. Everyone else only has it where they can use
    /// it: they aren't to know about the configuration.
    /// </summary>
    private HistoryButtonState DecideHistoryButtonState(EntryAccessInfo access, bool isDirectory)
    {
        if (!config.History.Enabled || !access.CanViewHistory)
            return HistoryButtonState.Hidden;

        if (history.IsKept(PhysicalPath, isDirectory))
            return HistoryButtonState.Usable;

        return tree.CanManageConfiguration(User)
            ? new HistoryButtonState(IsShown: true, $"The configuration leaves this {(isDirectory ? "directory" : "file")} out of the history (gitignore).")
            : HistoryButtonState.Hidden;
    }

    /// <summary>An address with <c>?before=</c> or <c>?commit=</c>: the entry as it was, if the user may see it so.</summary>
    private async Task<IActionResult> OnGetEarlierStateAsync()
    {
        // They are about the notes as they are now; and a page shows one state, not two
        if (Request.Query.ContainsKey("config") || Request.Query.ContainsKey("selection")
            || (Request.Query.ContainsKey(EarlierStateParameter.Before) && Request.Query.ContainsKey(EarlierStateParameter.Commit)))
            return BadRequest();

        // The changes of a commit (they are no state that could be downloaded)
        string? commit = Request.Query[EarlierStateParameter.Commit];
        bool showsChanges = Request.Query.ContainsKey(CommitChangesRequests.ChangesParameter);
        if (showsChanges && Request.Query.ContainsKey("download"))
            return BadRequest();

        EarlierStateAnswer answer = showsChanges
            ? commitChangesRequests.AnswerRequestForChanges(User, VirtualPath, commit!)
            : earlierStateRequests.AnswerRequestForEarlierState(User, VirtualPath, Request.Query[EarlierStateParameter.Before], commit);
        switch (answer)
        {
            case AnswerWithStatus status:
                return StatusCode(status.StatusCode);
            case RedirectToNow redirect:
                EarlierStateNotice.Set(HttpContext, redirect.Notice);
                return Redirect(AddressOfNow());
            case RedirectToStateOfCommit redirect:
                EarlierStateNotice.Set(HttpContext, redirect.Notice);
                string now = AddressOfNow();
                return Redirect(now + (now.Contains('?') ? "&" : "?") + EarlierStateParameter.ForCommit(commit!.ToLowerInvariant()).QueryPart);
            case ShowEntryAsItWas shown:
                // The state is read from until the response is sent (a zip is written while it is sent)
                Response.RegisterForDispose(shown.State);
                return await ShowEntryAsItWasAsync(shown);
            case ShowChangesOfCommit changes:
                Response.RegisterForDispose(changes.State);
                if (changes.Previous is not null)
                    Response.RegisterForDispose(changes.Previous);
                return await ShowChangesOfCommitAsync(changes);
            default:
                throw new InvalidOperationException("Unknown answer to a request for an earlier state.");
        }
    }

    private async Task<IActionResult> ShowChangesOfCommitAsync(ShowChangesOfCommit shown)
    {
        PhysicalPath = shown.PhysicalPath;

        // The bytes of a side of the change, for the browser to show it
        if (Request.Query.ContainsKey("raw"))
            return CommitChangesRequests.RawBytesOfSide(Response, shown, previous: Request.Query.ContainsKey(CommitChangesRequests.PreviousParameter));

        string addressOfNow = AddressOfNow();
        EarlierStateCard = earlierStateViews.CreateCard(User, shown.State, shown.Shown, addressOfNow, showsChanges: true);
        if (shown.IsDirectory)
            Directory = commitChangesViews.CreateDirectoryView(User, VirtualPath, shown, addressOfNow);
        else
            FileChanges = await commitChangesViews.CreateFileChangesAsync(User, VirtualPath, shown, addressOfNow);
        return Page();
    }

    private async Task<IActionResult> ShowEntryAsItWasAsync(ShowEntryAsItWas shown)
    {
        PhysicalPath = shown.PhysicalPath;

        // The bytes and the download of the entry as it was, like those of the entry as it is
        if (Request.Query.ContainsKey("raw"))
            return EarlierStateRequests.RawBytesAsTheyWere(Response, shown);
        if (Request.Query.ContainsKey("download"))
            return earlierStateRequests.DownloadAsItWas(User, VirtualPath, shown);

        // Otherwise its page, in which nothing can be changed (?edit shows the source of a markdown file, read-only)
        string addressOfNow = AddressOfNow();
        EarlierStateCard = earlierStateViews.CreateCard(User, shown.State, shown.Shown, addressOfNow);
        if (shown.Entry.IsDirectory)
            Directory = earlierStateViews.CreateDirectoryView(User, VirtualPath, shown, addressOfNow);
        else
            File = await earlierStateViews.CreateFileViewAsync(User, VirtualPath, shown, addressOfNow, Request.Query.ContainsKey("edit"), Request.Host.Host);
        return Page();
    }

    /// <summary><c>?history</c>: the times at which the entry can be seen as it was, for the dialog of the history button.</summary>
    private IActionResult OnGetTimesAtWhichEntryCanBeSeen()
    {
        if (earlierStateRequests.TimesAtWhichEntryCanBeSeen(User, VirtualPath) is not { } ranges)
            return NotFound();

        return new JsonResult(new
        {
            ranges = ranges.Select(range => new { after = EarlierStateTime.Format(range.After), until = range.Until is { } until ? EarlierStateTime.Format(until) : null })
        });
    }

    /// <summary>
    /// <c>?commits</c>: a page (<c>&amp;page=</c>, from 1) of the commits that changed the entry, for the sidebar of the list of changes (as its
    /// HTML), from the state that is shown (the <c>?before=</c> or <c>?commit=</c> it is asked with) back; with <c>&amp;newer</c>, the lines of
    /// those right after that state instead.
    /// </summary>
    private IActionResult OnGetCommitsThatChangedEntry()
    {
        int page = int.TryParse(Request.Query[_CommitSidebar.PageParameter], out int asked) && asked >= 1 ? asked : 1;
        EarlierStateParameter? shown = Request.Query[EarlierStateParameter.Commit].FirstOrDefault() is { } commit
            ? EarlierStateParameter.ForCommit(commit.ToLowerInvariant())
            : Request.Query[EarlierStateParameter.Before].FirstOrDefault() is { } before ? new EarlierStateParameter(EarlierStateParameter.Before, before) : null;
        bool newer = Request.Query.ContainsKey(_CommitSidebar.NewerParameter);
        if (earlierStateRequests.CommitsThatChangedEntry(User, VirtualPath, shown, page, newer) is not { } found)
            return NotFound();

        string? shownCommitId = shown is { IsCommit: true } ? shown.Value : null;
        return newer
            ? Partial("/Features/HistoryViewing/Commits/_CommitLines.cshtml", _CommitLines.Of(found, shownCommitId))
            : Partial("/Features/HistoryViewing/Commits/_CommitList.cshtml", _CommitList.Of(found, shownCommitId));
    }

    /// <summary>
    /// The address of this page without <c>?before=</c>, <c>?commit=</c> and what goes with a commit (<c>&amp;changes</c>, <c>&amp;previous</c>;
    /// the rest of the query as it is): the entry as it is now.
    /// </summary>
    public string AddressOfNow()
    {
        string[] stateParameters = [EarlierStateParameter.Before, EarlierStateParameter.Commit, CommitChangesRequests.ChangesParameter, CommitChangesRequests.PreviousParameter];
        // The other parts of the query are kept as they were written (not encoded again), so that only the state goes
        string[] kept = [.. (Request.QueryString.Value ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Split('=')[0] is var name && !stateParameters.Contains(name, StringComparer.OrdinalIgnoreCase))];
        return Request.PathBase.ToUriComponent() + Request.Path.ToUriComponent() + (kept.Length > 0 ? "?" + string.Join('&', kept) : "");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // An earlier state can't be changed, by anyone (the same answer whatever the path, so it tells nothing about it)
        if (EarlierStateParameter.IsIn(Request.Query))
            return BadRequest(EarlierStateViews.CantBeChangedReason);

        IActionResult? notFound = ResolveForChange();
        if (notFound is not null)
            return notFound;

        if (Request.Query.ContainsKey("config"))
            return tree.CanManageConfiguration(User) ? await configHandler.SaveAsync(Request, PhysicalPath) : NotFound("Path not found.");

        string? selection = Request.Query["selection"];
        if (selection is not null)
            return await selectionHandler.HandlePostAsync(Request, User, selection);

        string? transfer = Request.Query["transfer"];
        if (transfer is not null)
            return await transferHandler.HandleAsync(Request, User, PhysicalPath, transfer);

        string? create = Request.Query["create"];
        if (create is not null)
            return await createHandler.HandleAsync(User, PhysicalPath, create);

        string? upload = Request.Query["upload"];
        if (upload is not null)
            return await uploadHandler.HandleAsync(Request, User, PhysicalPath, upload);

        string? rename = Request.Query["rename"];
        if (rename is not null)
            return await renameHandler.HandleAsync(User, VirtualPath, PhysicalPath, rename);

        return await saveHandler.HandleAsync(User, PhysicalPath, Request.Body);
    }

    public async Task<IActionResult> OnDeleteAsync()
    {
        if (EarlierStateParameter.IsIn(Request.Query))
            return BadRequest(EarlierStateViews.CantBeChangedReason);

        IActionResult? notFound = ResolveForChange();
        if (notFound is not null)
            return notFound;

        return await deleteHandler.HandleAsync(User, VirtualPath, PhysicalPath);
    }

    /// <summary>
    /// Finds the entry a POST or DELETE is for. Returns the answer if there is none the user may know of. Whether they may read it (and
    /// whatever else the operation needs) is up to the operation.
    /// </summary>
    private IActionResult? ResolveForChange()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        if (VirtualPath.StartsWith("_/"))
            return NotFound("Page not found.");

        string? physicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        if (physicalPath is null || !tree.IsKnown(User, physicalPath))
            return NotFound("Path not found.");

        PhysicalPath = physicalPath;
        return null;
    }
}
