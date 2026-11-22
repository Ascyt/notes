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
public sealed record ArchiveRequest(string? Format, string? Compression, string? Password, long? Version);

/// <summary>
/// Answers <c>?archive</c>, the download as an archive with the options of the dialog (<see cref="_ArchiveModal"/>), in two steps, so that the
/// password never is in an address:
/// <list type="bullet">
/// <item><c>POST /dir?archive=directory</c> makes an archive of the directory, <c>POST ?archive=selection</c> one of the user's selection
/// (of whichever entry it is asked). It answers once the archive is made, with <c>{ "id": …, "leftOutUnreadable": bool }</c>.</item>
/// <item><c>GET ?archive=&lt;id&gt;</c> then downloads it, for the user who made it only, until it is deleted an hour later.</item>
/// </list>
/// </summary>
public sealed class ArchiveHandler(EntryLocations locations, IPathService path, AccessTree tree, ISelectionService selection,
    PreparedArchives archives, DownloadHandler downloads, ArchiveTexts texts, EntryTexts entryTexts, ILogger<ArchiveHandler> logger)
    : IEntryGetAction, IEntryPostAction
{
    public const string ArchiveParameter = "archive";

    /// <summary>The options and a version are all a body has: far less than this.</summary>
    private const long MaxBodyBytes = 4096;

    public string Parameter => ArchiveParameter;

    /// <summary>The directory to archive is read, and the other requests come from the page the user has open.</summary>
    public AccessNeeded Needs => AccessNeeded.Readable;

    public Task<IActionResult> GetAsync(EntryRequest request)
    {
        if (SelectionHandler.UserIdOf(request.User) is not { } userId || !Guid.TryParse(request.Value, out Guid id))
            return Task.FromResult<IActionResult>(new BadRequestObjectResult(entryTexts.InvalidRequest));

        if (archives.Find(userId, id) is not { } archive)
            return Task.FromResult<IActionResult>(new NotFoundObjectResult(texts.ArchiveGone));

        return Task.FromResult<IActionResult>(new PhysicalFileResult(archive.PhysicalPath, "application/octet-stream")
        {
            FileDownloadName = archive.FileName,
            EnableRangeProcessing = true
        });
    }

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        if (request.Value is not ("directory" or "selection"))
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        // Without an id it couldn't be downloaded by its maker alone (and the cookie scheme would answer a Forbid with a redirect)
        if (SelectionHandler.UserIdOf(request.User) is not { } userId)
            return new ObjectResult(texts.LoginRequired) { StatusCode = StatusCodes.Status403Forbidden };

        if (await ReadBodyAsync(request.Context) is not { } body
            || ArchiveOptions.Parse(body.Format, body.Compression, body.Password) is not { } options)
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        using IDisposable? claim = archives.TryClaimMaking(userId);
        if (claim is null)
            return new ConflictObjectResult(texts.AlreadyPreparing);

        // What goes in, checked now; what is inside the directories is checked as the archive gets to it, and all of it again at the end
        (ArchiveContents? contents, string? name, IActionResult? refusal) = request.Value == "directory"
            ? OfDirectory(request)
            : OfSelection(request.User, userId, body.Version);
        if (refusal is not null)
            return refusal;

        return await MakeAsync(userId, contents!, name! + options.Extension, options, request.Context.RequestAborted);
    }

    private (ArchiveContents?, string?, IActionResult?) OfDirectory(EntryRequest request)
    {
        if (!Directory.Exists(request.PhysicalPath))
            return (null, null, new BadRequestObjectResult(texts.NotADirectory));

        return (ArchiveContents.OfDirectory(request.User, tree, locations, request.PhysicalPath),
            downloads.DownloadNameOf(request.VirtualPath, locations.NameOf(request.PhysicalPath)), null);
    }

    private (ArchiveContents?, string?, IActionResult?) OfSelection(ClaimsPrincipal user, string userId, long? version)
    {
        SelectionSnapshot snapshot = selection.Get(userId);
        if (snapshot.Busy)
            return (null, null, new ConflictObjectResult(texts.SelectionBusy));
        if (snapshot.Version != version)
            return (null, null, new ConflictObjectResult(texts.SelectionChanged));
        if (snapshot.Paths.Count == 0)
            return (null, null, new BadRequestObjectResult(texts.NothingSelected));

        // Each selected entry must still be there and openable. One that the user may no longer know of isn't named.
        IReadOnlyList<FileSystemInfo?> found = path.FindEntries(snapshot.Paths);
        List<FileSystemInfo> entries = [];
        foreach (FileSystemInfo? entry in found)
        {
            if (entry is null || !tree.IsKnown(user, entry))
                return (null, null, new ConflictObjectResult(texts.SelectedEntryGone));
            if (!tree.CanOpen(user, entry))
                return (null, null, new ObjectResult(texts.NoReadAccess(tree.Display(entry.FullName)))
                    { StatusCode = StatusCodes.Status403Forbidden });
            entries.Add(entry);
        }

        // Named after the directory they are in, where they are all in the same one
        string[] parents = [.. snapshot.Paths.Select(selected => selected.Contains('/') ? selected[..selected.LastIndexOf('/')] : "")
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        string parentPath = parents.Length == 1 ? parents[0] : "";
        string parentName = parentPath == "" ? "" : parentPath[(parentPath.LastIndexOf('/') + 1)..];
        return (ArchiveContents.OfEntries(user, tree, locations, entries), downloads.DownloadNameOf(parentPath, parentName), null);
    }

    private async Task<IActionResult> MakeAsync(string userId, ArchiveContents contents, string fileName, ArchiveOptions options,
        CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        string target = archives.NewPath(id, options.Extension);
        try
        {
            using (await archives.WaitForTurnAsync(cancellationToken))
                await ArchiveWriter.WriteAsync(contents.Folder, options, target, cancellationToken);

            // Checked again at the end, as the permissions may have changed while it was written
            if (!contents.StillReadable())
            {
                DeleteQuietly(target);
                return new ConflictObjectResult(texts.PermissionsChanged);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not make an archive at {path}", target);
            DeleteQuietly(target);
            return new ObjectResult(texts.ServerCantRead) { StatusCode = StatusCodes.Status500InternalServerError };
        }
        catch
        {
            // Cut off (the page was left) or failed otherwise: nothing is kept of it
            DeleteQuietly(target);
            throw;
        }

        PreparedArchive archive = archives.Add(id, userId, target, fileName);
        return new JsonResult(new { id = archive.Id, contents.LeftOutUnreadable });
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

    private static void DeleteQuietly(string path)
    {
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
