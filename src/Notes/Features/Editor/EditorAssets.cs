namespace Notes.Features.Editor;

/// <summary>
/// The scripts of the editors, by their paths in the library (see <see cref="Components.Assets.ComponentAssets"/>): the code editor (Monaco)
/// that the page of a file, the configuration page and the changes of a commit share, and what the page of a file adds to it.
/// </summary>
public static class EditorAssets
{
    /// <summary>The code editor, its zoom and the shortcuts it leaves to the page.</summary>
    public const string Editor = "Features/Editor/editor.js";

    /// <summary>Where the editor opens in a file: the place the preview was opened from.</summary>
    public const string StartPosition = "Features/Editor/editor-start-position.js";

    /// <summary>Tab and Shift+Tab, and the indent buttons, in the simple editor.</summary>
    public const string TextAreaIndent = "Features/Editor/text-area-indent.js";

    /// <summary>Lists in Markdown carried on with Enter.</summary>
    public const string MarkdownLists = "Features/Editor/markdown-lists.js";
}
