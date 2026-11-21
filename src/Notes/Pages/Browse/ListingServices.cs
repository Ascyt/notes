using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Assets;
using Notes.Pages.Browse.EntryTable;
using Notes.Pages.Browse.MediaGrid;

namespace Notes.Pages.Browse;

public static class ListingServices
{
    /// <summary>The listing of a directory: its texts (the site's to replace, see <see cref="ListingTexts"/>) and the styles of its parts.</summary>
    public static IServiceCollection AddListing(this IServiceCollection services)
    {
        services.TryAddSingleton<ListingTexts>();
        return services.AddComponentStyles(DirectoryView.Styles, _EntryGroup.Styles, _MediaGroup.Styles);
    }
}
