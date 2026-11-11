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
using notes.Features.HistoryViewing;
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
    EarlierStateViews earlierStateViews) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public DirectoryView? Directory { get; private set; }
    public new FileView? File { get; private set; }

    /// <summary>The editor of the configuration of the entry (<c>?config</c>), instead of the entry itself.</summary>
    public ConfigEditorView? ConfigEditor { get; private set; }

    /// <summary>The time of the earlier state that is shown (<c>?before=</c>), or null for the notes as they are.</summary>
    public DateTimeOffset? Before { get; private set; }

    /// <summary>Why the page was sent here from an earlier state (<see cref="EarlierStateNotice"/>), shown as an error message.</summary>
    public string? EarlierStateMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        // No body, so the error page is shown instead
        if (VirtualPath.StartsWith("_/"))
            return NotFound();

        // Earlier states have their own rules, also for paths that aren't there any more (docs/development/history-specification.md)
        if (Request.Query.ContainsKey(_HistoryModal.BeforeParameter))
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

    /// <summary>An address with <c>?before=</c>: the entry as it was, if the user may see it so.</summary>
    private async Task<IActionResult> OnGetEarlierStateAsync()
    {
        // They are about the notes as they are now
        if (Request.Query.ContainsKey("config") || Request.Query.ContainsKey("selection"))
            return BadRequest();

        switch (earlierStateRequests.AnswerRequestForEarlierState(User, VirtualPath, Request.Query[_HistoryModal.BeforeParameter]))
        {
            case AnswerWithStatus status:
                return StatusCode(status.StatusCode);
            case RedirectToNow redirect:
                EarlierStateNotice.Set(HttpContext, redirect.Notice);
                return Redirect(AddressWithoutBefore());
            case ShowEntryAsItWas shown:
                // The state is read from until the response is sent (a zip is written while it is sent)
                Response.RegisterForDispose(shown.State);
                return await ShowEntryAsItWasAsync(shown);
            default:
                throw new InvalidOperationException("Unknown answer to a request for an earlier state.");
        }
    }

    private async Task<IActionResult> ShowEntryAsItWasAsync(ShowEntryAsItWas shown)
    {
        PhysicalPath = shown.PhysicalPath;
        Before = shown.Before;

        // The bytes and the download of the entry as it was, like those of the entry as it is
        if (Request.Query.ContainsKey("raw"))
            return EarlierStateRequests.RawBytesAsTheyWere(Response, shown);
        if (Request.Query.ContainsKey("download"))
            return earlierStateRequests.DownloadAsItWas(User, VirtualPath, shown);

        // Otherwise its page, in which nothing can be changed (?edit only shows what it shows without)
        if (shown.Entry.IsDirectory)
            Directory = earlierStateViews.CreateDirectoryView(User, VirtualPath, shown);
        else
            File = await earlierStateViews.CreateFileViewAsync(User, VirtualPath, shown, Request.Query.ContainsKey("edit"), Request.Host.Host);
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

    /// <summary>The address of this page without <c>?before=</c> (the rest of the query as it is): the entry as it is now.</summary>
    public string AddressWithoutBefore()
    {
        // The other parts of the query are kept as they were written (not encoded again), so that only the time goes
        string[] kept = [.. (Request.QueryString.Value ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !string.Equals(part.Split('=')[0], _HistoryModal.BeforeParameter, StringComparison.OrdinalIgnoreCase))];
        return Request.PathBase.ToUriComponent() + Request.Path.ToUriComponent() + (kept.Length > 0 ? "?" + string.Join('&', kept) : "");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // An earlier state can't be changed, by anyone (the same answer whatever the path, so it tells nothing about it)
        if (Request.Query.ContainsKey(_HistoryModal.BeforeParameter))
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
        if (Request.Query.ContainsKey(_HistoryModal.BeforeParameter))
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
