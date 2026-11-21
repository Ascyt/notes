using System.Security.Claims;

namespace notes.Features.EntryActions;

/// <summary>A request for an <see cref="IEntryAction"/>, with the entry it is for.</summary>
/// <param name="VirtualPath">The path of the entry as the address has it, without slashes at the start and end ("" for the root).</param>
/// <param name="PhysicalPath">Where the entry is (or would be) on disk.</param>
/// <param name="Value">The value of the action's parameter (<c>?rename=new.md</c> has "new.md"), "" if it has none or for a default action.</param>
public sealed record EntryRequest(HttpContext Context, string VirtualPath, string PhysicalPath, string Value)
{
    public ClaimsPrincipal User => Context.User;
    public HttpRequest Request => Context.Request;
    public HttpResponse Response => Context.Response;
    public bool IsRoot => VirtualPath == "";
}
