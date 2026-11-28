using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Components.Download;
using Notes.Features.Components.Selection;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Archive;

/// <summary>
/// Body of a request for an archive: the options of the dialog (see <see cref="ArchiveOptions.Parse"/>) and, for the selection, the version of
/// it that the page showed.
/// </summary>
/// <param name="AsIs">
/// For admins: the archived folders in it (virtual paths) that go in as they are kept, their configuration and their archive, instead of what is
/// in them. The other archived folders in it are unzipped for it.
/// </param>
public sealed record ArchiveRequest(string? Format, string? Compression, string? Password, long? Version, IReadOnlyList<string>? AsIs = null);

/// <summary>An archived folder in what is downloaded, as the dialog lists it (<c>GET ?archive=folders</c>).</summary>
/// <param name="Path">Its virtual path, as <see cref="ArchiveRequest.AsIs"/> names it.</param>
/// <param name="Label">Where it is in the download: its path from the directory, or from where the selection is.</param>
/// <param name="Inside">The path of the listed archived folder it is in, if any: kept as it is, that one holds this one.</param>
public sealed record ArchivedFolderInDownload(string Path, string Label, bool Zipped, bool Encrypted, string? Inside);

/// <summary>
/// Answers <c>?archive</c>, the download as an archive with the options of the dialog (<see cref="_ArchiveModal"/>), in two steps, so that the
/// password never is in an address:
/// <list type="bullet">
/// <item><c>POST /dir?archive=directory</c> makes an archive of the directory, <c>POST ?archive=selection</c> one of the user's selection
/// (of whichever entry it is asked). It answers once the archive is made, with <c>{ "id": …, "leftOutUnreadable": bool }</c>. With
/// <c>Prefer: respond-async</c> (what the page sends) it answers at once instead, with 202 and <c>{ "id": … }</c>: a big archive takes
/// longer than a proxy waits for an answer.</item>
/// <item><c>GET ?archive=&lt;id&gt;&amp;progress</c> says how far it got: <c>{ "done": bool, "progress": 0…1 }</c>, and once it is done
/// <c>leftOutUnreadable</c>, or the refusal it ended with. The page asks every so often; one that isn't asked for a minute is cancelled.</item>
/// <item><c>GET ?archive=&lt;id&gt;</c> then downloads it, for the user who made it only, until it is deleted an hour later.</item>
/// <item><c>POST /dir?archive=as-is</c>, for admins, of an archived folder: its own archive as it is kept (zipped first, if it is unzipped),
/// copied to be downloaded like the others, named after the folder. The body needs no options.</item>
/// <item><c>GET ?archive=folders</c> (of the directory) and <c>GET ?archive=selection-folders</c> (of the selection) list the archived folders
/// in it, for the dialog: <c>{ "keepsAsIs": bool, "folders": [<see cref="ArchivedFolderInDownload"/>] }</c>. Admins may have them go in as
/// they are kept (<see cref="ArchiveRequest.AsIs"/>); for everyone else they are unzipped for it and zipped again later.</item>
/// </list>
/// </summary>
public sealed class ArchiveHandler(EntryLocations locations, IPathService path, AccessTree tree, ISelectionService selection,
    PreparedArchives archives, DownloadHandler downloads, ArchiveTexts texts, EntryTexts entryTexts, ZippedFolders.ArchivedFolders archivedFolders,
    ZippedFolders.ArchivePasswordPrompt passwordPrompt, Passwords.PasswordLocks passwordLocks, ILogger<ArchiveHandler> logger)
    : IEntryGetAction, IEntryPostAction
{
    public const string ArchiveParameter = "archive";
    public const string AsIsValue = "as-is";

    /// <summary>How much of the as-is download's bar is zipping the folder (where it is unzipped); copying the archive is the rest.</summary>
    private const double AsIsZipShare = 0.8;

    /// <summary>The options and a version are all a body has: far less than this.</summary>
    private const long MaxBodyBytes = 4096;

    public string Parameter => ArchiveParameter;

    /// <summary>The directory to archive is read, and the other requests come from the page the user has open.</summary>
    public AccessNeeded Needs => AccessNeeded.Readable;

    public const string ProgressParameter = "progress";

    public const string FoldersValue = "folders";
    public const string SelectionFoldersValue = "selection-folders";

    public Task<IActionResult> GetAsync(EntryRequest request)
    {
        if (request.Value is FoldersValue or SelectionFoldersValue && SelectionHandler.UserIdOf(request.User) is { } asker)
            return Task.FromResult(FoldersIn(request, asker));

        if (SelectionHandler.UserIdOf(request.User) is not { } userId || !Guid.TryParse(request.Value, out Guid id))
            return Task.FromResult<IActionResult>(new BadRequestObjectResult(entryTexts.InvalidRequest));

        if (request.Request.Query.ContainsKey(ProgressParameter))
            return Task.FromResult(ProgressOf(userId, id));

        if (archives.Find(userId, id) is not { } archive)
            return Task.FromResult<IActionResult>(archives.FindJob(userId, id) is { Finished.IsCompleted: false }
                ? new ConflictObjectResult(texts.StillPreparing)
                : new NotFoundObjectResult(texts.ArchiveGone));

        return Task.FromResult<IActionResult>(new PhysicalFileResult(archive.PhysicalPath, "application/octet-stream")
        {
            FileDownloadName = archive.FileName,
            EnableRangeProcessing = true
        });
    }

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        if (request.Value is not ("directory" or "selection" or AsIsValue))
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        // Without an id it couldn't be downloaded by its maker alone (and the cookie scheme would answer a Forbid with a redirect)
        if (SelectionHandler.UserIdOf(request.User) is not { } userId)
            return new ObjectResult(texts.LoginRequired) { StatusCode = StatusCodes.Status403Forbidden };

        if (request.Value == AsIsValue)
            return await AsIsAsync(request, userId);

        if (await ReadBodyAsync(request.Context) is not { } body
            || ArchiveOptions.Parse(body.Format, body.Compression, body.Password) is not { } options)
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        // Given back once the archive is made (the job holds it), or right away if nothing is
        IDisposable? claim = archives.TryClaimMaking(userId);
        if (claim is null)
            return new ConflictObjectResult(texts.AlreadyPreparing);

        // What goes in, checked now; what is inside the directories is checked as the archive gets to it, and all of it again at the end
        (ArchiveContents? contents, string? name, IReadOnlyList<string>? topLevel, IActionResult? refusal) = request.Value == "directory"
            ? OfDirectory(request)
            : OfSelection(request.Context, request.User, userId, body.Version);
        if (refusal is not null)
        {
            claim.Dispose();
            return refusal;
        }

        // The archived folders that go in as they are kept (admins only), and are zipped for it
        IReadOnlyList<string> requested = body.AsIs ?? [];
        List<string> keptAsIs = [];
        if (requested.Count > 0)
        {
            if (!request.User.IsInRole(AppAccess.AdminRole))
            {
                claim.Dispose();
                return new ObjectResult(texts.AsIsAdminsOnly) { StatusCode = StatusCodes.Status403Forbidden };
            }
            HashSet<string> inIt = new(archivedFolders.ArchivedAtOrBelow(topLevel!), StringComparer.OrdinalIgnoreCase);
            foreach (string virtualPath in requested)
            {
                if (path.ToPhysicalPath(virtualPath) is not { } physical || !inIt.Contains(archivedFolders.OuterOf(physical)))
                {
                    claim.Dispose();
                    return new BadRequestObjectResult(texts.NotArchivedInIt);
                }
                keptAsIs.Add(archivedFolders.OuterOf(physical));
            }
            if (archivedFolders.UnknownPasswordToZipAtOrBelow(keptAsIs.Select(folder => archivedFolders.MountedAt(folder)?.PhysicalPath ?? folder))
                is { } passwordNeeded)
            {
                claim.Dispose();
                return passwordPrompt.Ask(request.Context, passwordNeeded, request.PhysicalPath);
            }
        }
        contents!.KeepAsIs(keptAsIs, archivedFolders.OuterOf);

        // The others in it are unzipped for it (those the user may open, with their password unlocked), now, as whether a password is
        // unlocked is a matter of this request. A folder kept as it is, and what is in it, stays as it is.
        bool IsKeptAsIs(string folder) => keptAsIs.Any(kept => locations.IsSameOrInside(folder, kept)
                                                             || locations.IsSameOrInside(folder, archivedFolders.MountedAt(kept)?.PhysicalPath ?? kept));
        try
        {
            await archivedFolders.UnzipAllAtOrBelowAsync(topLevel!, folder => !IsKeptAsIs(folder) && tree.CanOpen(request.User, folder)
                                                                              && passwordLocks.FirstLocked(folder) is null);
        }
        catch (IOException e)
        {
            claim.Dispose();
            logger.LogError(e, "An archived folder in a download of {path} could not be unzipped", request.PhysicalPath);
            return new ObjectResult(texts.ArchivedFolderUnzipFailed) { StatusCode = StatusCodes.Status500InternalServerError };
        }

        string fileName = name! + options.Extension;
        return await StartAsync(request, userId, claim,
            (job, cancellationToken) => MakeAsync(job, userId, contents!, topLevel!, keptAsIs, fileName, options, cancellationToken));
    }

    /// <summary>
    /// The archived folders in the directory or the selection, for the dialog (see <see cref="ArchivedFolderInDownload"/>). Only those the
    /// user may open; those inside one that is zipped are only known once it is unzipped (they are in its archive).
    /// </summary>
    private IActionResult FoldersIn(EntryRequest request, string userId)
    {
        IReadOnlyList<string> topLevel;
        string from;
        if (request.Value == FoldersValue)
        {
            if (!Directory.Exists(request.PhysicalPath))
                return new BadRequestObjectResult(texts.NotADirectory);
            topLevel = [request.PhysicalPath];
            from = request.VirtualPath.Trim('/');
        }
        else
        {
            SelectionSnapshot snapshot = selection.Get(userId);
            topLevel = [.. snapshot.Paths.Select(path.ToPhysicalPath).OfType<string>()];
            string[] parents = [.. snapshot.Paths.Select(selected => selected.Contains('/') ? selected[..selected.LastIndexOf('/')] : "")
                .Distinct(StringComparer.OrdinalIgnoreCase)];
            from = parents.Length == 1 ? parents[0] : "";
        }

        List<ArchivedFolderInDownload> folders = [];
        List<string> listed = [];
        foreach (string folder in archivedFolders.ArchivedAtOrBelow(topLevel).Order(StringComparer.OrdinalIgnoreCase))
        {
            string where = archivedFolders.MountedAt(folder)?.PhysicalPath ?? folder;
            if (!tree.CanOpen(request.User, where))
                continue;
            string virtualPath = path.ToVirtualPath(where) is var found && found == "." ? "" : found.Replace('\\', '/');
            string label = from == "" ? virtualPath : virtualPath.Length > from.Length ? virtualPath[(from.Length + 1)..] : locations.NameOf(where);
            string? inside = listed.LastOrDefault(outer => virtualPath.StartsWith(outer + "/", StringComparison.OrdinalIgnoreCase));
            folders.Add(new ArchivedFolderInDownload(virtualPath, label == "" ? locations.NameOf(where) : label,
                archivedFolders.IsZipped(folder), archivedFolders.IsEncrypted(folder), inside));
            listed.Add(virtualPath);
        }
        return new JsonResult(new { keepsAsIs = request.User.IsInRole(AppAccess.AdminRole), folders });
    }

    /// <summary>
    /// Starts making it, apart from the request (which may be cut off by a proxy long before a big archive is done), and answers at once
    /// (where the request asks for that) or once it is made.
    /// </summary>
    private async Task<IActionResult> StartAsync(EntryRequest request, string userId, IDisposable claim,
        Func<ArchiveJob, CancellationToken, Task<ArchiveJobResult>> work)
    {
        // What goes in is only read as the archive gets to it, which may be after this request was answered (respond-async): it reads with the
        // unlocks of this request's browser, as they are then (outside a request, nothing would be unlocked)
        Func<Passwords.Protection, bool> unlocks = passwordLocks.UnlocksForLater();
        ArchiveJob job = archives.Start(Guid.NewGuid(), userId, claim, async (job, cancellationToken) =>
        {
            using IDisposable carried = passwordLocks.WithUnlocks(unlocks);
            return await work(job, cancellationToken);
        }, request.PhysicalPath);

        if (RespondsAtOnce(request.Request))
            return new ObjectResult(new { id = job.Id }) { StatusCode = StatusCodes.Status202Accepted };

        // (A request that is cut off stops waiting, and the job is then cancelled like one the page left)
        using (job.Waiting())
            return Answer(await job.Finished.WaitAsync(request.Context.RequestAborted));
    }

    /// <summary>
    /// An archived folder's own archive, as it is kept (with the configurations and the history in it), for admins: everything in it goes,
    /// whoever may read it, so it is theirs only. The folder is zipped first if it is unzipped (what counts is then in its copy), and the
    /// archive copied, so that zipping it again later never replaces what is being downloaded.
    /// </summary>
    private async Task<IActionResult> AsIsAsync(EntryRequest request, string userId)
    {
        if (!request.User.IsInRole(AppAccess.AdminRole))
            return new ObjectResult(texts.AsIsAdminsOnly) { StatusCode = StatusCodes.Status403Forbidden };

        string folder = archivedFolders.OuterOf(request.PhysicalPath);
        if (!Directory.Exists(request.PhysicalPath) || archivedFolders.ArchiveStateOf(folder) is null)
            return new BadRequestObjectResult(texts.NotArchived);

        // Unzipped and encrypted, with a password the app doesn't know: asked for now, as it couldn't be zipped
        if (archivedFolders.UnknownPasswordToZipAtOrBelow([request.PhysicalPath]) is { } passwordNeeded)
            return passwordPrompt.Ask(request.Context, passwordNeeded, request.PhysicalPath);

        if (archives.TryClaimMaking(userId) is not { } claim)
            return new ConflictObjectResult(texts.AlreadyPreparing);

        string name = downloads.DownloadNameOf(request.VirtualPath, locations.NameOf(folder));
        return await StartAsync(request, userId, claim, (job, cancellationToken) => GuardedAsync(job, async keepTarget =>
        {
            // Where there is zipping to do, it is most of the wait: the bar goes through it, then through the copy
            double zipShare = archivedFolders.ArchiveStateOf(folder) == ZippedFolders.ArchiveState.Zipped ? 0 : AsIsZipShare;

            // (Waits for what uses it, a while at most: the request that asked for this holds no use of it, see ArchiveGate. Opening the
            // folder meanwhile stops it: who opens it needs it now, and this ends as "in use")
            if (!await archivedFolders.ZipAsync(folder, stopsWhenOpened: true, report: done => job.Report(done * zipShare), status: job.SetStatus))
                return new ArchiveJobResult(null, false, StatusCodes.Status409Conflict, texts.ArchivedFolderInUse);
            if (ZippedFolders.ArchivedFolders.ArchiveFileOf(folder) is not { } kept)
                return new ArchiveJobResult(null, false, StatusCodes.Status409Conflict, texts.NotArchived);

            string extension = Path.GetExtension(kept);
            string target = keepTarget(archives.NewPath(job.Id, extension));
            long size = new FileInfo(kept).Length;
            await CopyAsync(kept, target, done =>
            {
                job.Report(zipShare + done * (1 - zipShare));
                job.SetStatus(new WorkStatus(WorkPhases.Copying, Bytes: (long)(done * size), TotalBytes: size));
            }, cancellationToken);
            if (job.IsAbandoned)
                throw new OperationCanceledException();
            return new ArchiveJobResult(archives.Add(job.Id, userId, target, name + extension), false);
        }));
    }

    /// <summary>
    /// Copies the archive, saying how far it got. Read so that it may be replaced or deleted meanwhile (zipping the folder again renames a new
    /// archive over it), which leaves what is read as it was.
    /// </summary>
    private static async Task CopyAsync(string source, string target, Action<double> report, CancellationToken cancellationToken)
    {
        const int bufferSize = 1 << 20;
        await using FileStream from = new(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize, useAsync: true);
        await using FileStream to = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        long total = Math.Max(1, from.Length);
        long copied = 0;
        byte[] buffer = new byte[bufferSize];
        int read;
        while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            report((double)copied / total);
        }
    }

    /// <summary>Whether the request asks to be answered before the archive is made (RFC 7240), to then ask how far it got.</summary>
    private static bool RespondsAtOnce(HttpRequest request)
        => request.Headers["Prefer"].Any(value => value?.Split(',').Any(part => part.Trim().Equals("respond-async",
            StringComparison.OrdinalIgnoreCase)) == true);

    private IActionResult ProgressOf(string userId, Guid id)
    {
        if (archives.FindJob(userId, id) is not { } job)
            return new NotFoundObjectResult(texts.ArchiveGone);

        job.Asked();
        if (!job.Finished.IsCompleted)
            return new JsonResult(new { done = false, progress = job.Progress, status = job.Status });

        ArchiveJobResult result = job.Finished.Result;
        return result.Archive is null
            ? new ObjectResult(result.Failure) { StatusCode = result.FailureStatus }
            : new JsonResult(new { done = true, progress = 1.0, result.LeftOutUnreadable });
    }

    /// <summary>The answer once it is made: its id, or the refusal it ended with.</summary>
    private static IActionResult Answer(ArchiveJobResult result)
        => result.Archive is null
            ? new ObjectResult(result.Failure) { StatusCode = result.FailureStatus }
            : new JsonResult(new { id = result.Archive.Id, result.LeftOutUnreadable });

    /// <returns>What goes in, the name of the archive and the physical paths at its top level (to measure them), or why nothing does.</returns>
    private (ArchiveContents?, string?, IReadOnlyList<string>?, IActionResult?) OfDirectory(EntryRequest request)
    {
        if (!Directory.Exists(request.PhysicalPath))
            return (null, null, null, new BadRequestObjectResult(texts.NotADirectory));

        return (ArchiveContents.OfDirectory(request.User, tree, locations, request.PhysicalPath),
            downloads.DownloadNameOf(request.VirtualPath, locations.NameOf(request.PhysicalPath)), [request.PhysicalPath], null);
    }

    private (ArchiveContents?, string?, IReadOnlyList<string>?, IActionResult?) OfSelection(HttpContext context, ClaimsPrincipal user,
        string userId, long? version)
    {
        SelectionSnapshot snapshot = selection.Get(userId);
        if (snapshot.Busy)
            return (null, null, null, new ConflictObjectResult(texts.SelectionBusy));
        if (snapshot.Version != version)
            return (null, null, null, new ConflictObjectResult(texts.SelectionChanged));
        if (snapshot.Paths.Count == 0)
            return (null, null, null, new BadRequestObjectResult(texts.NothingSelected));

        // Each selected entry must still be there and openable. One that the user may no longer know of isn't named.
        IReadOnlyList<FileSystemInfo?> found = path.FindEntries(snapshot.Paths);
        List<FileSystemInfo> entries = [];
        foreach (FileSystemInfo? entry in found)
        {
            if (entry is null || !tree.IsKnown(user, entry))
                return (null, null, null, new ConflictObjectResult(texts.SelectedEntryGone));
            // One a password locks for this browser (it can be selected): its password is asked for, and the request sent again
            if (passwordLocks.FirstLocked(entry.FullName) is { } locked)
                return (null, null, null, passwordPrompt.Ask(context, locked, entry.FullName));
            if (!tree.CanOpen(user, entry))
                return (null, null, null, new ObjectResult(texts.NoReadAccess(tree.Display(entry.FullName)))
                    { StatusCode = StatusCodes.Status403Forbidden });
            entries.Add(entry);
        }

        // Named after the directory they are in, where they are all in the same one
        string[] parents = [.. snapshot.Paths.Select(selected => selected.Contains('/') ? selected[..selected.LastIndexOf('/')] : "")
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        string parentPath = parents.Length == 1 ? parents[0] : "";
        string parentName = parentPath == "" ? "" : parentPath[(parentPath.LastIndexOf('/') + 1)..];
        return (ArchiveContents.OfEntries(user, tree, locations, entries), downloads.DownloadNameOf(parentPath, parentName),
            [.. entries.Select(entry => entry.FullName)], null);
    }

    private Task<ArchiveJobResult> MakeAsync(ArchiveJob job, string userId, ArchiveContents contents, IReadOnlyList<string> topLevel,
        IReadOnlyList<string> keptAsIs, string fileName, ArchiveOptions options, CancellationToken cancellationToken)
        => GuardedAsync(job, async keepTarget =>
        {
            // What goes in as it is kept is its archive: zipped first, where it is unzipped (opening it meanwhile stops that, as for the
            // as-is download of the folder itself)
            foreach (string folder in keptAsIs)
                if (!await archivedFolders.ZipAsync(folder, stopsWhenOpened: true,
                        status: zipping => job.SetStatus(new WorkStatus(WorkPhases.ZippingKept, zipping.Files, zipping.TotalFiles))))
                    return new ArchiveJobResult(null, false, StatusCodes.Status409Conflict, texts.ArchivedFolderInUse);

            string target = keepTarget(archives.NewPath(job.Id, options.Extension));
            // The archived folders it reads stay unzipped until it is done (the request that asked for it may be answered long before)
            using IDisposable uses = await archivedFolders.UseAllAroundAsync(topLevel);
            using (await archives.WaitForTurnAsync(cancellationToken, job))
            {
                job.SetStatus(new WorkStatus(WorkPhases.Measuring));
                (long bytes, int files) = ArchiveProgress.Measure(locations, topLevel);
                await ArchiveWriter.WriteAsync(contents.Folder, options, target, cancellationToken,
                    new ArchiveProgress(bytes, files, job.Report, job.SetStatus, options.Password is null ? WorkPhases.Writing : WorkPhases.Encrypting));
            }

            // (The progress only says so when it moves: the last of it may have been read after the page was left)
            if (job.IsAbandoned)
                throw new OperationCanceledException();

            // Checked again at the end, as the permissions may have changed while it was written
            if (!contents.StillReadable())
            {
                DeleteQuietly(target);
                return new ArchiveJobResult(null, false, StatusCodes.Status409Conflict, texts.PermissionsChanged);
            }

            return new ArchiveJobResult(archives.Add(job.Id, userId, target, fileName), contents.LeftOutUnreadable);
        });

    /// <summary>
    /// Runs the making of an archive, and turns what goes wrong into its refusal, deleting what was written (the file that
    /// <paramref name="work"/> says it writes to, through the function it is given).
    /// </summary>
    private async Task<ArchiveJobResult> GuardedAsync(ArchiveJob job, Func<Func<string, string>, Task<ArchiveJobResult>> work)
    {
        string? target = null;
        try
        {
            return await work(path => target = path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not make the archive {id} at {path}", job.Id, target);
            DeleteQuietly(target);
            return new ArchiveJobResult(null, false, StatusCodes.Status500InternalServerError, texts.ServerCantRead);
        }
        catch (OperationCanceledException)
        {
            // The page was left (nobody asked for it a while), or the app stops: nothing is kept of it
            DeleteQuietly(target);
            return new ArchiveJobResult(null, false, StatusCodes.Status410Gone, texts.ArchiveGone);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Making the archive {id} at {path} failed", job.Id, target);
            DeleteQuietly(target);
            return new ArchiveJobResult(null, false, StatusCodes.Status500InternalServerError, texts.ServerCantRead);
        }
    }

    /// <summary>The body as JSON, read up to <see cref="MaxBodyBytes"/>; null if it is none such.</summary>
    private static async Task<ArchiveRequest?> ReadBodyAsync(HttpContext context)
    {
        // ReadFromJsonAsync throws something other than a JsonException for a body that isn't sent as JSON
        if (!context.Request.HasJsonContentType())
            return null;

        // Read completely, but never beyond the limit
        byte[] buffer = new byte[MaxBodyBytes + 1];
        int length = 0;
        int read;
        while (length < buffer.Length && (read = await context.Request.Body.ReadAsync(buffer.AsMemory(length), context.RequestAborted)) > 0)
            length += read;
        if (length > MaxBodyBytes)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ArchiveRequest>(buffer.AsSpan(0, length), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>As ASP.NET reads JSON: names in camelCase.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static void DeleteQuietly(string? path)
    {
        if (path is null)
            return;
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left to PreparedArchives.DeleteStale
        }
    }
}
