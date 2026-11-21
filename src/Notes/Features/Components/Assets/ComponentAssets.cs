namespace Notes.Features.Components.Assets;

/// <summary>
/// The styles and scripts of the components, each kept in the folder of its component and named by its path in the library (such as
/// <c>Features/Components/Toolbar/_Toolbar.css</c>, served at <c>/_content/Notes/</c> and that path). The stylesheets are linked on every
/// page (<see cref="StylesToLink"/>), a script where its component is. A site puts a file of its own in place of one of them, or nothing,
/// and adds stylesheets of its own after them, which override what they set (docs/library.md):
/// <code>
/// builder.Services.Configure&lt;ComponentAssets&gt;(assets => assets
///     .Replace(_Toolbar.Styles, "/css/toolbar.css")
///     .AddStyles("/css/notes.css"));
/// </code>
/// </summary>
public sealed class ComponentAssets
{
    /// <summary>Where the site serves the static assets of the library.</summary>
    public static readonly string LibraryAddress = $"/_content/{typeof(ComponentAssets).Assembly.GetName().Name}/";

    private readonly List<string> _componentStyles = [];
    private readonly List<string> _siteStyles = [];
    private readonly Dictionary<string, string?> _replacements = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>For the features: the stylesheet of a component (its path in the library), linked on every page.</summary>
    public ComponentAssets AddComponentStyles(string path)
    {
        _componentStyles.Add(path);
        return this;
    }

    /// <summary>For a site: a stylesheet of its own (an address, such as <c>/css/notes.css</c>), linked after those of the components.</summary>
    public ComponentAssets AddStyles(string address)
    {
        _siteStyles.Add(address);
        return this;
    }

    /// <summary>
    /// For a site: a file of its own (an address) in place of a style or script of a component (by its path in the library), or nothing for
    /// null. Its component then looks and works as the site's file has it.
    /// </summary>
    public ComponentAssets Replace(string path, string? address)
    {
        _replacements[path] = address;
        return this;
    }

    /// <summary>
    /// The addresses of the stylesheets every page links: those of the components (or what the site put in their place), then the site's own.
    /// </summary>
    /// <param name="ofThePage">Those that only the page needs (<see cref="PageStylesTagHelper"/>), linked after the components'.</param>
    public IEnumerable<string> StylesToLink(IEnumerable<string> ofThePage)
        => _componentStyles.Concat(ofThePage).Distinct().Select(AddressOf).OfType<string>().Concat(_siteStyles);

    /// <summary>Where a style or script of a component (by its path in the library) is loaded from, or null where the site left it out.</summary>
    public string? AddressOf(string path)
        => _replacements.TryGetValue(path, out string? address) ? address : LibraryAddress + path;
}

public static class ComponentAssetExtensions
{
    /// <summary>For the features: the stylesheets of their components (paths in the library), linked on every page.</summary>
    public static IServiceCollection AddComponentStyles(this IServiceCollection services, params string[] paths)
        => services.Configure<ComponentAssets>(assets =>
        {
            foreach (string path in paths)
                _ = assets.AddComponentStyles(path);
        });
}
