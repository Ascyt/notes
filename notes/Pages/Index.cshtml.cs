using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using notes.Features.Auth;
using notes.Features.Components.Create;
using notes.Features.Components.Delete;
using notes.Features.Components.Download;
using notes.Features.Components.Rename;
using notes.Features.Components.Upload;
using notes.Features.Helpers;
using notes.Features.View.Browse;
using notes.Features.View.Editor;

namespace notes.Pages;

/// <summary>
/// The page for every file and directory of the notes. It only finds what the address points at and passes the
/// request on to the feature that answers it (the query string says which one).
/// </summary>
[Authorize(Policy = NotesAccess.PolicyName)]
public sealed class IndexModel(Config config,
    IPathService path,
    DownloadHandler downloadHandler,
    CreateHandler createHandler,
    UploadHandler uploadHandler,
    RenameHandler renameHandler,
    SaveHandler saveHandler,
    DeleteHandler deleteHandler) : PageModel
{
    public string VirtualPath { get; private set; } = "";
    public string PhysicalPath { get; private set; } = "";
    public DirectoryView? Directory { get; private set; }
    public new FileView? File { get; private set; }

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

        if (Request.Query.ContainsKey("download"))
            return downloadHandler.Handle(VirtualPath, PhysicalPath, User.IsInRole(NotesAccess.AdminRole));

        if (System.IO.Directory.Exists(PhysicalPath))
        {
            Directory = new DirectoryView(config, path)
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath,
                CanBrowse = User.IsInRole(NotesAccess.AdminRole)
            };
            Directory.InitAsync();
        }
        else if (System.IO.File.Exists(PhysicalPath))
        {
            File = new FileView
            {
                VirtualPath = VirtualPath,
                PhysicalPath = PhysicalPath
            };
            await File.InitAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        if (VirtualPath.StartsWith("_/"))
            return NotFound("Page not found.");

        string? physicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        if (physicalPath is null)
            return NotFound("Path not found.");

        PhysicalPath = physicalPath;

        string? create = Request.Query["create"];
        if (create is not null)
            return createHandler.Handle(PhysicalPath, create);

        string? upload = Request.Query["upload"];
        if (upload is not null)
            return await uploadHandler.HandleAsync(Request, User, PhysicalPath, upload);

        string? rename = Request.Query["rename"];
        if (rename is not null)
            return renameHandler.Handle(VirtualPath, PhysicalPath, rename);

        return await saveHandler.HandleAsync(PhysicalPath, Request.Body);
    }

    public IActionResult OnDelete()
    {
        VirtualPath = Request.Path.Value!.TrimStart('/').TrimEnd('/');
        if (VirtualPath.StartsWith("_/"))
            return NotFound("Page not found.");

        string? physicalPath = path.ToPhysicalPath(config.Dir, VirtualPath);
        if (physicalPath is null)
            return NotFound("Path not found.");

        PhysicalPath = physicalPath;

        return deleteHandler.Handle(VirtualPath, PhysicalPath);
    }
}
