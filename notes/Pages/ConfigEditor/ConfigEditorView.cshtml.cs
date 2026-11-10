using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.ConfigEditor;

/// <summary>The page that edits the configuration of a folder or a file (<c>?config</c>), for those who may manage it.</summary>
public class ConfigEditorView : PageModel
{
    public string VirtualPath { get; init; } = "";

    /// <summary>Whether it is the configuration of a file (otherwise of a folder).</summary>
    public bool IsFile { get; init; }

    /// <summary>What the editor starts with: the configuration as it is saved, or the default if there is none.</summary>
    public string Contents { get; init; } = "";

    /// <summary>What the editor is reset to.</summary>
    public string DefaultContents { get; init; } = "";

    /// <summary>The JSON schema the editor completes and checks against.</summary>
    public string Schema { get; init; } = "";

    /// <summary>What is wrong with the saved configuration (which until it is fixed leaves everyone but admins without access), or null if nothing.</summary>
    public string? Warning { get; init; }
}
