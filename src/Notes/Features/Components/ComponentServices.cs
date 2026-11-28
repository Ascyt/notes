using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Archive;
using Notes.Features.Components.Assets;
using Notes.Features.Components.Breadcrumb;
using Notes.Features.Components.Busy;
using Notes.Features.Components.Create;
using Notes.Features.Components.Delete;
using Notes.Features.Components.Download;
using Notes.Features.Components.Rename;
using Notes.Features.Components.Selection;
using Notes.Features.Components.Toast;
using Notes.Features.Components.Toolbar;
using Notes.Features.Components.Transfer;
using Notes.Features.Components.Upload;
using Notes.Features.EntryActions;

namespace Notes.Features.Components;

public static class ComponentServices
{
    /// <summary>
    /// The parts of the pages: the toolbar and the breadcrumb, and those that change or download the entries (selection, copy, move, delete,
    /// create, rename, upload, download).
    /// </summary>
    public static IServiceCollection AddComponents(this IServiceCollection services)
    {
        // The selections live in memory of this process only: fine for a single instance, but a restart forgets them and several instances
        // would not share them. For that, ISelectionService needs a shared store (e.g. a distributed cache).
        _ = services.AddSingleton<ISelectionService, SelectionService>();
        _ = services.AddSingleton<TransferPlanner>();
        _ = services.AddSingleton<TransferExecutor>();
        _ = services.AddSingleton<ITransferService, TransferService>();
        _ = services.AddSingleton<IZipService, ZipService>();
        _ = services.AddSingleton<IUploadService, UploadService>();
        _ = services.AddSingleton<UploadedArchiveUnzipper>();
        _ = services.AddSingleton<SelectionStatusReader>();
        _ = services.AddSingleton<Operations.StorageOperations>();
        services.TryAddSingleton<Operations.OperationTexts>();
        _ = services.AddScoped<ClearStaleUploadsJob>();
        _ = services.AddSingleton<PreparedArchives>();
        _ = services.AddScoped<ClearStaleArchivesJob>();

        // The parts every page has. Their texts are the site's to replace (registered before or after this, see ToolbarTexts)
        services.TryAddSingleton<ToolbarTexts>();
        services.TryAddSingleton<BreadcrumbTexts>();
        services.TryAddSingleton<RenameTexts>();
        services.TryAddSingleton<CreateTexts>();
        services.TryAddSingleton<ToastTexts>();
        services.TryAddSingleton<SelectionTexts>();
        services.TryAddSingleton<UploadTexts>();
        services.TryAddSingleton<TransferTexts>();
        services.TryAddSingleton<ArchiveTexts>();
        services.TryAddSingleton<BusyTexts>();
        services.TryAddSingleton<ProgressTexts>();
        _ = services.AddSingleton<ToolbarButtons>();
        _ = services.AddComponentStyles(_Toolbar.Styles, _Breadcrumb.Styles, _RenameInput.Styles, _Toasts.Styles,
            _SelectionBar.Styles, _Upload.Styles, _BusyOverlay.Styles);

        return services
            .AddEntryAction<SelectionHandler>()
            .AddEntryAction<TransferHandler>()
            .AddEntryAction<DownloadHandler>()
            .AddEntryAction<ArchiveHandler>()
            .AddEntryAction<UploadHandler>()
            .AddEntryAction<UnzippingUploadHandler>()
            .AddEntryAction<PieceHashesHandler>()
            .AddEntryAction<Operations.OperationsHandler>()
            .AddEntryAction<CreateHandler>()
            .AddEntryAction<RenameHandler>()
            .AddEntryAction<DeleteHandler>();
    }
}
