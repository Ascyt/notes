using Microsoft.AspNetCore.Mvc;

namespace Notes.Features.EntryActions;

/// <summary>What the user must be allowed for an action to be asked for at all. Anything beyond it is checked by the action itself.</summary>
public enum AccessNeeded
{
    /// <summary>The user may know of the entry (<see cref="Auth.AccessTree.IsKnown(System.Security.Claims.ClaimsPrincipal, string)"/>).</summary>
    Known,

    /// <summary>The user may also read the entry. Otherwise 403, or 404 for the root, which everybody may know of.</summary>
    Readable,

    /// <summary>
    /// The user may manage the configuration. For everyone else there is no such thing, which is answered like an entry that doesn't exist.
    /// </summary>
    ManagesConfiguration,
}

/// <summary>
/// Something that can be done with an entry, asked for by a parameter of the query (<c>/docs?download</c>). The page of the
/// entry (<see cref="Pages.IndexModel"/>) has made sure the user may know of the entry and has the <see cref="Needs">access it needs</see>
/// before it hands the request on. A request asks for at most one action.
/// </summary>
/// <remarks>Registered with <see cref="EntryActionServices.AddEntryAction{TAction}"/>; an action may answer several methods.</remarks>
public interface IEntryAction
{
    /// <summary>
    /// The parameter of the query that asks for it, or null for the action of its method when no parameter asks for another (saving the
    /// file for POST, deleting for DELETE).
    /// </summary>
    string? Parameter { get; }

    AccessNeeded Needs { get; }
}

/// <summary>An action asked for with GET. The entry exists.</summary>
public interface IEntryGetAction : IEntryAction
{
    Task<IActionResult> GetAsync(EntryRequest request);
}

/// <summary>An action asked for with POST. The entry may not exist: whether it has to is up to the action.</summary>
public interface IEntryPostAction : IEntryAction
{
    Task<IActionResult> PostAsync(EntryRequest request);
}

/// <summary>An action asked for with DELETE. The entry may not exist, as for POST.</summary>
public interface IEntryDeleteAction : IEntryAction
{
    Task<IActionResult> DeleteAsync(EntryRequest request);
}
