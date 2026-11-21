using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.HistoryViewing.Views;

/// <summary>The choice of an earlier state of the current file or directory, behind the History button of the toolbar.</summary>
public class _HistoryModal : PageModel
{
    /// <summary>The styles and the script of the choice of a time, by their paths in the library (see <see cref="Notes.Features.Components.Assets.ComponentAssets"/>).</summary>
    public const string Styles = "Features/HistoryViewing/Views/_HistoryModal.css";
    public const string Script = "Features/HistoryViewing/Views/_HistoryModal.js";

    /// <summary>The query parameter that asks for the times at which the entry can be seen as it was (as JSON), which the dialog shows.</summary>
    public const string VisibleTimesParameter = "history";

    /// <summary>What is looked at ("file" or "directory"), used in the text.</summary>
    public string Kind { get; init; } = "";
}
