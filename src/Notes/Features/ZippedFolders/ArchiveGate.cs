using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.Helpers;
using Notes.Features.Passwords;

namespace Notes.Features.ZippedFolders;

/// <summary>What <see cref="ArchiveGate.EnterAsync"/> decided: go on (the folders along the path are unzipped), or answer with this.</summary>
/// <param name="Result">The answer instead, or null to go on.</param>
/// <param name="Page">For a page: what it shows while the folder is unzipped (with <see cref="Result"/> null).</param>
public sealed record ArchiveGateAnswer(IActionResult? Result, _Unzipping? Page)
{
    public static ArchiveGateAnswer GoOn { get; } = new(null, null);
}

/// <summary>
/// In front of every request of an entry (<see cref="Pages.IndexModel"/>): an archived folder along its path that is zipped is unzipped
/// first (<see cref="ArchivedFolders"/>), and every unzipped one along it is held in use until the request is answered, so that it is not
/// zipped under it. What takes longer than <see cref="ArchivesConfig.WaitSeconds"/> is answered instead:
/// <list type="bullet">
/// <item>a page, with the page that shows how far it got (<see cref="_Unzipping"/>);</item>
/// <item>anything else with 503, <see cref="UnzippingHeader"/> (the address of the folder) and <c>Retry-After</c>: the page's scripts wait
/// and send it again (<c>_Unzipping.js</c>), a program asks again later;</item>
/// <item><c>GET &lt;folder&gt;?unzip</c> with how far it got: <c>{ "done": bool, "progress": 0…1 }</c>.</item>
/// </list>
/// Only who may open the folder (and has its password, where it has one) gets it unzipped. For everyone else the request goes on as if it
/// were zipped: what is in it isn't found, which is answered as for anything that isn't there (or with the password prompt).
/// </summary>
public sealed class ArchiveGate(ArchivedFolders archives, AccessTree tree, PasswordLocks passwordLocks, PasswordUnlocks unlocks,
    Configuration.FolderConfigStore configs, IPathService paths, ArchivesConfig settings, UnzipTexts texts)
{
    public const string UnzipParameter = "unzip";
    public const string UnzippingHeader = "X-Notes-Unzipping";
    private const string RenameParameter = "rename";

    /// <summary>
    /// The actions that work on an archived folder as a whole (its configuration, its name, its place in the selection, its password), for
    /// which it isn't unzipped when it is the entry of the request. Anything else (listing it, downloading what is in it, putting something
    /// into it) needs what is in it.
    /// </summary>
    private static readonly string[] WholeFolderParameters = ["config", "selection", "unlock", "lock"];

    /// <summary>
    /// <c>?archive</c> needs what is in the entry only to make an archive of the directory or of the selection (which may be in it): asking
    /// how far an archive got, downloading it, or downloading the folder's own archive as it is (which zips it) works on it as a whole.
    /// </summary>
    private const string ArchiveParameter = "archive";
    private static readonly string[] ArchivesOfContents = ["directory", "selection"];

    /// <param name="isPage">Whether a page is asked for (a GET without an action), which shows how far it got while it waits.</param>
    public async Task<ArchiveGateAnswer> EnterAsync(HttpContext context, string virtualPath, bool isPage)
    {
        HttpRequest request = context.Request;
        // Zipping what is unzipped (ZipHandler) holds no use of anything, which would keep it from being zipped, and unzips nothing
        if (HttpMethods.IsPost(request.Method) && request.Query.ContainsKey(ZipHandler.ZipParameter))
            return ArchiveGateAnswer.GoOn;
        // Neither does asking how far zipping got (ZippingHandler): a use, or unzipping, would stop the zipping it asks about
        if (HttpMethods.IsGet(request.Method) && request.Query.ContainsKey(ZippingHandler.ZippingParameter))
            return ArchiveGateAnswer.GoOn;

        bool asksProgress = HttpMethods.IsGet(request.Method) && request.Query.ContainsKey(UnzipParameter);
        bool worksOnWholeFolder = HttpMethods.IsDelete(request.Method)
            || WholeFolderParameters.Any(parameter => request.Query.ContainsKey(parameter))
            || (request.Query.TryGetValue(ArchiveParameter, out var archive) && !ArchivesOfContents.Contains(archive.ToString()));
        bool includingTarget = asksProgress || !worksOnWholeFolder;
        // A rename checks what is in the entry (unzipped for it), then zips it itself (it moves as its archive): no use of it is held
        bool usingTarget = !(HttpMethods.IsPost(request.Method) && request.Query.ContainsKey(RenameParameter));

        // (Real time: this is how long a person waits, not something the app's clock decides)
        Task waited = Task.Delay(TimeSpan.FromSeconds(settings.WaitSeconds));

        for (;;)
        {
            (string? zipped, IDisposable uses) = await archives.EnterAsync(virtualPath, includingTarget, usingTarget);
            if (zipped is null)
            {
                // Held until the answer is sent (a download is read while it is)
                context.Response.RegisterForDispose(uses);
                return asksProgress ? new ArchiveGateAnswer(new JsonResult(new { done = true, progress = 1.0 }), null) : ArchiveGateAnswer.GoOn;
            }
            uses.Dispose();

            // Not to be unzipped by whoever may not see what is in it: for them it stays as it is (and the password is asked for first)
            if (!tree.CanOpen(context.User, zipped) || passwordLocks.FirstLocked(zipped) is not null)
                return ArchiveGateAnswer.GoOn;

            UnzipJob job = archives.Unzip(zipped);
            if (await Task.WhenAny(job.Finished, waited) == job.Finished)
            {
                // Encrypted, and its password isn't known (the app restarted, say) though the browser has it unlocked: asked for it again,
                // which tells the app the password (ArchivePasswords). The request goes on as for a zipped folder, to the password prompt.
                if (job.NeedsPasswordOf is { } passwordFolder && configs.Get(passwordFolder).Config?.PasswordHash is { } hash)
                {
                    unlocks.Lock(context, new Protection(passwordFolder, hash));
                    return ArchiveGateAnswer.GoOn;
                }
                if (await job.Finished is not null)
                    return new ArchiveGateAnswer(new ObjectResult(texts.Failed) { StatusCode = StatusCodes.Status500InternalServerError }, null);
                // Unzipped: looked at again (an archived folder inside it may be next)
                continue;
            }

            string display = paths.ToVirtualPath(zipped) is var shown and not "." ? shown : "";
            string address = VirtualPaths.ToAddress(paths.ToVirtualPath(zipped));
            if (asksProgress)
                return new ArchiveGateAnswer(new JsonResult(new { done = false, progress = job.Progress, status = job.Status }), null);
            if (isPage)
                return new ArchiveGateAnswer(null, new _Unzipping { Address = address, Display = display, Progress = job.Progress,
                    Status = job.Status });

            context.Response.Headers[UnzippingHeader] = address;
            context.Response.Headers.RetryAfter = "2";
            return new ArchiveGateAnswer(new ObjectResult(texts.Unzipping.Replace("{entry}", display))
                { StatusCode = StatusCodes.Status503ServiceUnavailable }, null);
        }
    }
}
