using notes.Features.EntryActions;

namespace notes.Features.Editor;

public static class EditorServices
{
    /// <summary>Saving a file from the editor (the POST of the file itself).</summary>
    public static IServiceCollection AddEditor(this IServiceCollection services)
        => services.AddEntryAction<SaveHandler>();
}
