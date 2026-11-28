using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// The page of an entry in an archived folder that is still being unzipped: it names the folder and shows how far it got (asking
/// <c>?unzip</c> of the folder, <see cref="ArchiveGate"/>), and loads the page again once it is done.
/// </summary>
public class _Unzipping : PageModel
{
    /// <summary>The script that waits for an unzipping, on every page (also where a request of the page's scripts needs one).</summary>
    public const string Script = "Features/ZippedFolders/_Unzipping.js";

    /// <summary>The address of the archived folder.</summary>
    public string Address { get; init; } = "";

    /// <summary>Its virtual path, as the user sees it.</summary>
    public string Display { get; init; } = "";

    /// <summary>How far it got when the page was made, 0 to 1.</summary>
    public double Progress { get; init; }

    /// <summary>What it was doing then, for the text under the bar (which the script keeps up to date).</summary>
    public Helpers.WorkStatus Status { get; init; } = new(Helpers.WorkPhases.Waiting);
}
