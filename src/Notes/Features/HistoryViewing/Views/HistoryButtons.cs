using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.History;

namespace Notes.Features.HistoryViewing.Views;

/// <summary>Decides whether the page of an entry as it is now has the way to its earlier states (<see cref="HistoryButtonState"/>).</summary>
public sealed class HistoryButtons(Config config, EntryLocations locations, AccessTree tree, HistoryService history, HistoryTexts texts)
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
        EntryLocation? location = locations.LocationOf(physicalPath);
        if (location is not { IsRoot: true })
        {
            return tree.CanManageConfiguration(user)
                ? new HistoryButtonState(IsShown: true, location?.KeepsHistory == true
                    ? texts.BridgeHistoryNotViewable
                    : texts.BridgeWithoutHistory)
                : HistoryButtonState.Hidden;
        }

        if (history.IsKept(physicalPath, isDirectory))
            return HistoryButtonState.Usable;

        return tree.CanManageConfiguration(user)
            ? new HistoryButtonState(IsShown: true,
                texts.LeftOut(isFile: !isDirectory))
            : HistoryButtonState.Hidden;
    }
}
