using notes.Features.Components.Create;
using notes.Features.Components.Delete;
using notes.Features.Components.Download;
using notes.Features.Components.Rename;
using notes.Features.Components.Selection;
using notes.Features.Components.Transfer;
using notes.Features.Components.Upload;
using notes.Features.EntryActions;

namespace notes.Features.Components;

public static class ComponentServices
{
    /// <summary>The parts of the pages that change or download the notes: selection, copy, move, delete, create, rename, upload, download.</summary>
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
        _ = services.AddScoped<ClearStaleUploadsJob>();

        return services
            .AddEntryAction<SelectionHandler>()
            .AddEntryAction<TransferHandler>()
            .AddEntryAction<DownloadHandler>()
            .AddEntryAction<UploadHandler>()
            .AddEntryAction<CreateHandler>()
            .AddEntryAction<RenameHandler>()
            .AddEntryAction<DeleteHandler>();
    }
}
