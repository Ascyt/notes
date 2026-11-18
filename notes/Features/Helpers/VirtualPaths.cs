namespace notes.Features.Helpers;

/// <summary>Virtual paths of the notes as addresses.</summary>
public static class VirtualPaths
{
    /// <summary>
    /// The address of the entry at the virtual path (as <see cref="IPathService.ToVirtualPath"/> gives it): <c>/</c> and each name escaped. A name
    /// may contain what means something in an address (<c>#</c>, <c>%</c>, <c>?</c> where the file system allows it), which must stay part of
    /// the name instead of starting a fragment or a query, or being decoded into something else.
    /// </summary>
    public static string ToAddress(string virtualPath)
        => virtualPath == "." ? "/" : "/" + string.Join('/', virtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
}
