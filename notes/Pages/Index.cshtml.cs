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
    HistoryService history) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public DirectoryView? Directory { get; private set; }
    public new FileView? File { get; private set; }

    /// <summary>The editor of the configuration of the entry (<c>?config</c>), instead of the entry itself.</summary>
    public ConfigEditorView? ConfigEditor { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        // No body, so the error page is shown instead
        if (VirtualPath.StartsWith("_/"))
            return NotFound();

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

        bool isDirectory = System.IO.Directory.Exists(PhysicalPath);
        HistoryOffer historyOffer = OfferHistory(access, isDirectory);

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
                History = historyOffer,
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
                History = historyOffer,
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
    private HistoryOffer OfferHistory(EntryAccessInfo access, bool isDirectory)
    {
        if (!config.History.Enabled || !access.CanViewHistory)
            return HistoryOffer.None;

        if (history.Keeps(PhysicalPath, isDirectory))
            return HistoryOffer.Available;

        return tree.CanManageConfiguration(User)
            ? new HistoryOffer(IsOffered: true, $"The configuration leaves this {(isDirectory ? "directory" : "file")} out of the history (gitignore).")
            : HistoryOffer.None;
    }

    public async Task<IActionResult> OnPostAsync()
    {
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
