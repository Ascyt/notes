using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Assets;
using Notes.Features.EntryActions;

namespace Notes.Features.ZippedFolders;

public static class ZippedFolderServices
{
    /// <summary>
    /// The archived folders (docs/development/zipped-folders.md): zipping and unzipping them safely, when that is done, the gate in front of
    /// the requests that need one unzipped, the job that zips them again, and the archive button (zipping them at once). Settings that make no sense don't let the app start.
    /// </summary>
    public static IServiceCollection AddArchivedFolders(this IServiceCollection services, ArchivesConfig settings)
    {
        settings.Check();
        _ = services.AddSingleton(settings);
        _ = services.AddSingleton<FolderArchiver>();
        _ = services.AddSingleton<ArchiveSettingsResolver>();
        _ = services.AddSingleton<ArchivedFolders>();
        _ = services.AddSingleton<ArchiveGate>();
        _ = services.AddScoped<ZipIdleArchivesJob>();
        services.TryAddSingleton<UnzipTexts>();
        return services.AddComponentStyles("Features/ZippedFolders/_Unzipping.css").AddEntryAction<ZipHandler>()
            .AddEntryAction<ZippingHandler>();
    }
}
