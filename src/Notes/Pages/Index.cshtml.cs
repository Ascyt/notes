using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Notes.Features.Addresses;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Components.Selection;
using Notes.Features.Configuration;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;
using Notes.Features.HistoryViewing;
using Notes.Features.HistoryViewing.Changes;
using Notes.Features.HistoryViewing.Requests;
using Notes.Features.HistoryViewing.Views;
using Notes.Pages.Browse;
using Notes.Pages.ConfigEditor;
using Notes.Pages.Editor;

namespace Notes.Pages;

/// <summary>
/// The page for every file and directory. It only finds what the address points at and passes the request on to the feature
/// that answers it: the history (<see cref="HistoryRequests"/>), or the <see cref="IEntryAction"/> the query asks for. Without one, a GET
/// shows the entry.
/// </summary>
/// <remarks>
/// There is no gate in front of it: anyone may ask for anything, and what they get is decided per entry. Whatever the request, the user must
/// be allowed to know of the entry it is for (<see cref="AccessTree.IsKnown"/>); one they aren't is answered exactly like one that doesn't exist.
/// So is a path the site doesn't show (<see cref="ExposedFolders"/>), before anything else looks at it.
/// The action says what else it needs (<see cref="AccessNeeded"/>), which is checked here; everything beyond that is up to the action, see
/// docs/development/security-specification.md.
/// </remarks>
public sealed class IndexModel(Config config,
    ExposedFolders exposed,
    EntryLocations locations,
    IPathService path,
    AccessTree tree,
    EntryActions actions,
    ConfigHandler configHandler,
    HistoryRequests historyRequests,
    HistoryButtons historyButtons,
    ISelectionService selectionService,
    EntryTexts texts,
    HistoryTexts historyTexts) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public DirectoryView? Directory { get; private set; }
    public new FileView? File { get; private set; }

    /// <summary>The changes of a commit to a file (<c>?commit=&amp;changes</c>), instead of the file.</summary>
    public _FileChanges? FileChanges { get; private set; }

    /// <summary>The editor of the configuration of the entry (<c>?config</c>), instead of the entry itself.</summary>
    public ConfigEditorView? ConfigEditor { get; private set; }

    /// <summary>The card of the earlier state that is shown (<c>?before=</c> or <c>?commit=</c>), or null for the entries as they are.</summary>
    public _EarlierStateCard? EarlierStateCard { get; private set; }

    /// <summary>Why the page was sent here from an earlier state (<see cref="EarlierStateNotice"/>), shown as an error message.</summary>
    public string? EarlierStateMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (HasRepeatedParameter)
            return BadRequest();

        VirtualPath = VirtualPathOfRequest();
        // Not a path the site shows (or one an address of the app takes). No body, so the error page is shown instead.
        if (!exposed.IsExposed(VirtualPath))
            return NotFound();

        // Earlier states have their own rules, also for paths that aren't there any more
        if (HistoryRequests.IsFor(Request.Query))
            return ShowOrAnswer(await historyRequests.AnswerAsync(HttpContext, VirtualPath));

        string? physicalPath = path.ToPhysicalPath(VirtualPath);
        if (physicalPath is null || !System.IO.Path.Exists(physicalPath) || !tree.IsKnown(User, physicalPath))
            return NotFound();
        PhysicalPath = physicalPath;

        // The editor of the configuration is another view of the entry, not an action, but no more than one of them is asked for either
        ActionChoice<IEntryGetAction> choice = actions.Choose<IEntryGetAction>(Request.Query);
        bool asksForConfig = Request.Query.ContainsKey(ConfigHandler.ConfigParameter);
        if (choice.NamesSeveral || (asksForConfig && choice.Action is not null))
            return BadRequest();

        if (asksForConfig)
        {
            if (Refusal(AccessNeeded.ManagesConfiguration, forChange: false) is { } refused)
                return refused;
            ConfigEditor = configHandler.CreateView(VirtualPath, PhysicalPath);
            return Page();
        }

        if (choice.Action is { } action)
        {
            return Refusal(action.Needs, forChange: false) 
                ?? await action.GetAsync(new EntryRequest(HttpContext, VirtualPath, PhysicalPath, choice.Value));
        }

        return await ShowEntryAsync();
    }

    /// <summary>
    /// The page of the entry as it is now. The root page is the one everybody may open (it carries the login button); it shows nothing of
    /// the entries to those who may not read it. Any other entry the user knows of but may not read (only logged-in users know of such) is
    /// listed in its directory, but can't be opened.
    /// </summary>
    private async Task<IActionResult> ShowEntryAsync()
    {
        EntryAccessInfo access = tree.GetAccess(User, PhysicalPath);
        if (!access.CanRead && VirtualPath != "")
            return StatusCode(StatusCodes.Status403Forbidden);

        EarlierStateMessage = EarlierStateNotice.Take(HttpContext) is { } notice ? historyTexts.Notice(notice) : null;
        bool isDirectory = System.IO.Directory.Exists(PhysicalPath);
        HistoryButtonState historyButton = historyButtons.For(User, PhysicalPath, access, isDirectory);

        if (isDirectory)
        {
            string? userId = SelectionHandler.UserIdOf(User);
            Directory = new DirectoryView(locations, path, tree)
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath,
                Viewer = User,
                CanBrowse = access.CanRead,
                CanConfigure = tree.CanManageConfiguration(User),
                HistoryButton = historyButton,
                Selection = userId is null ? SelectionSnapshot.Empty : selectionService.Get(userId)
            };
            Directory.Init();
        }
        else if (System.IO.File.Exists(PhysicalPath))
        {
            string? deleteDeniedReason = tree.DeleteDeniedReason(User, PhysicalPath);
            File = new FileView
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath,
                MaxTextBytes = config.Limits.MaxTextBytes,
                CanWrite = access.CanWrite,
                CanConfigure = tree.CanManageConfiguration(User),
                MediaNeighbours = MediaEntries.IsMedia(locations.NameOf(PhysicalPath)) ? MediaEntries.NeighboursNow(User, PhysicalPath,
                    locations, tree, path) : null,
                HistoryButton = historyButton,
                EditRequested = Request.Query.ContainsKey("edit"),
                SiteHost = Request.Host.Host,
                DeleteDeniedReason = deleteDeniedReason,
                RenameDeniedReason = tree.RenameDeniedReason(User, PhysicalPath, deleteDeniedReason)
            };
            await File.InitAsync();
        }

        return Page();
    }

    /// <summary>The page of an earlier state, or the answer that the history gave instead.</summary>
    private IActionResult ShowOrAnswer(HistoryAnswer answer)
    {
        if (answer.Page is not { } shown)
            return answer.Result!;

        EarlierStateCard = shown.Card;
        Directory = shown.Directory;
        File = shown.File;
        FileChanges = shown.FileChanges;
        return Page();
    }

    public Task<IActionResult> OnPostAsync()
        => ChangeAsync<IEntryPostAction>((action, request) => action.PostAsync(request));

    public Task<IActionResult> OnDeleteAsync()
        => ChangeAsync<IEntryDeleteAction>((action, request) => action.DeleteAsync(request));

    /// <summary>A POST or DELETE: the action the query asks for (or the default one of the method), for an entry the user may know of.</summary>
    private async Task<IActionResult> ChangeAsync<TAction>(Func<TAction, EntryRequest, Task<IActionResult>> run) where TAction : class, IEntryAction
    {
        if (HasRepeatedParameter)
            return BadRequest(texts.RepeatedParameter);

        // An earlier state can't be changed, by anyone (the same answer whatever the path, so it tells nothing about it)
        if (EarlierStateParameter.IsIn(Request.Query))
            return BadRequest(historyTexts.CantBeChanged);

        ActionChoice<TAction> choice = actions.Choose<TAction>(Request.Query);
        if (choice.NamesSeveral)
            return BadRequest(texts.MoreThanOneAction);

        VirtualPath = VirtualPathOfRequest();
        if (!exposed.IsExposed(VirtualPath))
            return NotFound(texts.PathNotFound);

        // Whether the entry has to exist is up to the action (an upload finds out what is in the directory under the lock)
        string? physicalPath = path.ToPhysicalPath(VirtualPath);
        if (physicalPath is null || !tree.IsKnown(User, physicalPath))
            return NotFound(texts.PathNotFound);
        PhysicalPath = physicalPath;

        if (choice.Action is not { } action)
            return BadRequest(texts.NothingToBeDone);
        return Refusal(action.Needs, forChange: true) 
            ?? await run(action, new EntryRequest(HttpContext, VirtualPath, PhysicalPath, choice.Value));
    }

    /// <summary>The answer if the user lacks what the action needs beyond knowing of the entry, or null if they have it.</summary>
    /// <param name="forChange">For a POST or DELETE, whose answers say why in their body (the page's scripts show it).</param>
    private IActionResult? Refusal(AccessNeeded needs, bool forChange) => needs switch
    {
        AccessNeeded.Known => null,
        // The root is known to everybody, so not being allowed to read it tells nothing either
        AccessNeeded.Readable => tree.GetAccess(User, PhysicalPath).CanRead ? null
            : VirtualPath == "" ? NotFound() : StatusCode(StatusCodes.Status403Forbidden),
        // For everyone else there is no such thing, which is answered like a missing one
        AccessNeeded.ManagesConfiguration => tree.CanManageConfiguration(User) ? null
            : forChange ? NotFound(texts.PathNotFound) : NotFound(),
        _ => throw new InvalidOperationException($"Unknown access {needs}.")
    };

    private string VirtualPathOfRequest()
        => Request.Path.Value!.TrimStart('/').TrimEnd('/');


    /// <summary>
    /// Whether a parameter of the query is there more than once (<c>?rename=a&amp;rename=b</c>). Nothing the pages ask for does that, and it
    /// would be read as the values joined by a comma ("a,b"), which is a valid name: such a request is refused instead of guessed at.
    /// </summary>
    private bool HasRepeatedParameter => Request.Query.Any(parameter => parameter.Value.Count > 1);
}
