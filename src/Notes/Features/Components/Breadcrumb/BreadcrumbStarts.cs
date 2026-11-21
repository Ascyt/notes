using Notes.Features.Addresses;

namespace Notes.Features.Components.Breadcrumb;

/// <summary>
/// A link of the site shown before the start of every breadcrumb, as if it were a directory above it (<c>Config:BreadcrumbParents</c>): a
/// page of the site's own, never one of the entries.
/// </summary>
/// <param name="Address">An address of the site (<c>/</c>, <c>/projects</c>) or of another one (<c>https://…</c>).</param>
public sealed record BreadcrumbParent(string Name, string Address);

/// <summary>Where a breadcrumb starts: the directory the site shows that the entry is in, and the links shown before it.</summary>
/// <param name="RootVirtualPath">See <see cref="_Breadcrumb.RootVirtualPath"/>.</param>
public sealed record BreadcrumbStart(string RootVirtualPath, IReadOnlyList<BreadcrumbParent> VirtualParents)
{
    public static BreadcrumbStart AtTheRoot { get; } = new("", []);
}

/// <summary>Where the breadcrumbs start, for the pages that show one.</summary>
public sealed class BreadcrumbStarts
{
    private readonly ExposedFolders _exposed;
    private readonly BreadcrumbParent[] _parents;

    /// <exception cref="InvalidOperationException">If a link has no name, or no address that is one (the app doesn't start).</exception>
    public BreadcrumbStarts(Config config, ExposedFolders exposed)
    {
        _exposed = exposed;
        _parents = config.BreadcrumbParents;
        foreach (BreadcrumbParent parent in _parents)
        {
            if (string.IsNullOrWhiteSpace(parent.Name))
                throw Invalid(parent, "it has no name.");
            if (!IsAddress(parent.Address))
                throw Invalid(parent, "its address is none: one of the site starts with a single '/', one of another site with https:// or http://.");
        }
    }

    public BreadcrumbStart Of(string virtualPath) => new(_exposed.RootOf(virtualPath), _parents);

    // A local address, as Url.IsLocalUrl has it ("//" and "/\" lead to another host), or an absolute one of the web
    private static bool IsAddress(string? address)
        => address is not null
           && ((address.StartsWith('/') && !address.StartsWith("//", StringComparison.Ordinal) && !address.StartsWith("/\\",
               StringComparison.Ordinal))
               || (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http"));

    private static InvalidOperationException Invalid(BreadcrumbParent parent, string why)
        => new($"Config:BreadcrumbParents: '{parent.Name}' ({parent.Address}) can't be shown, as {why}");
}
