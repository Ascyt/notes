using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.EntryActions;

namespace Notes.Features.Media;

public static class MediaServices
{
    /// <summary>The bytes of a media file for the browser to show (<c>?raw</c>), and the thumbnail of an image (<c>?thumbnail</c>).</summary>
    public static IServiceCollection AddMedia(this IServiceCollection services)
    {
        services.TryAddSingleton<MediaTexts>();
        return services
            .AddEntryAction<RawHandler>()
            .AddEntryAction<ThumbnailHandler>();
    }
}
