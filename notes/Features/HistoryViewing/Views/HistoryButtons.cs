using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.History;

namespace notes.Features.HistoryViewing.Views;

/// <summary>Decides whether the page of an entry as it is now has the way to its earlier states (<see cref="HistoryButtonState"/>).</summary>
public sealed class HistoryButtons(Config config, NotesLocations locations, AccessTree tree, HistoryService history)
{
    /// <summary>
    /// Those who manage the configuration have it wherever there is a history, disabled with the reason where the configuration leaves the
    /// entry out of it. Everyone else only has it where they can use it: they aren't to know about the configuration.
    /// </summary>
    public HistoryButtonState For(ClaimsPrincipal user, string physicalPath, EntryAccessInfo access, bool isDirectory)
    {
        if (!config.History.Enabled || !access.CanViewHistory)
            return HistoryButtonState.Hidden;

        // The history of a bridge is recorded in a repository of its own, which can't be viewed yet. Bridges are how the site is run, so
        // everyone else sees no sign of one: for them, there is no history here, as without the permission.
        NotesLocation? location = locations.LocationOf(physicalPath);
        if (location is not { IsRoot: true })
        {
            return tree.CanManageConfiguration(user)
                ? new HistoryButtonState(IsShown: true, location?.KeepsHistory == true
                    ? "This is in a bridge (Config:Bridges), whose history is recorded but can't be viewed in the app yet."
                    : "This is in a bridge (Config:Bridges) without a history (History:Enabled is false).")
                : HistoryButtonState.Hidden;
        }

        if (history.IsKept(physicalPath, isDirectory))
            return HistoryButtonState.Usable;

        return tree.CanManageConfiguration(user)
            ? new HistoryButtonState(IsShown: true, $"The configuration leaves this {(isDirectory ? "directory" : "file")} out of the history (gitignore).")
            : HistoryButtonState.Hidden;
    }
}
