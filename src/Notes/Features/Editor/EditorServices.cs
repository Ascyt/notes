using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Assets;
using Notes.Features.EntryActions;
using Notes.Pages.Editor;

namespace Notes.Features.Editor;

public static class EditorServices
{
    /// <summary>
    /// Saving a file from the editor (the POST of the file itself), and the page of a file with its editors: its texts (the site's to replace,
    /// see <see cref="EditorTexts"/>) and styles.
    /// </summary>
    public static IServiceCollection AddEditor(this IServiceCollection services)
    {
        services.TryAddSingleton<EditorTexts>();
        return services
            .AddComponentStyles(FileView.Styles, FileView.BinaryStyles)
            .AddEntryAction<SaveHandler>();
    }
}
