using System.Text.RegularExpressions;

namespace Notes.Features.Addresses;

/// <summary>
/// The addresses of the app itself, outside the entries: its pages (<see cref="Config.AppPath"/>, <c>/_/Login</c> by default) and its scripts
/// and styles (<c>/_content/…</c>). They come first, so the names they take at the root are no part of the entries: a directory
/// of such a name is hidden (<see cref="ExposedFolders"/>), and no new entry at the root gets one (docs/development/security-specification.md,
/// "Pages outside the entries").
/// </summary>
public sealed partial class AppAddresses
{
    /// <summary>The pages of the app (Pages/App), each at <see cref="Of"/> its name.</summary>
    public static IReadOnlyList<string> PageNames { get; } = ["About", "Error", "Login", "Logout", "Shutdown", "Hangfire"];

    /// <summary>Where the library's static assets are served, whatever the address of the pages.</summary>
    public const string StaticAssetsName = "_content";

    /// <summary>The address of the pages: "" for the root, otherwise "/" and names, without a "/" at the end (e.g. "/_").</summary>
    public string PagesPath { get; }

    private readonly HashSet<string> _takenRootNames;

    /// <exception cref="InvalidOperationException">If the address isn't one (the app doesn't start).</exception>
    public AppAddresses(Config config)
    {
        // One "/" at the end is the same address ("/" is the root), more are none
        string path = config.AppPath.EndsWith('/') ? config.AppPath[..^1] : config.AppPath;
        if (path != "" && !PathPattern().IsMatch(path))
            throw new InvalidOperationException($"Config:AppPath ('{config.AppPath}') is not an address for the pages of the app: empty, or like /_ or /app (letters, digits, '_' and '-').");
        PagesPath = path;

        // At the root, the pages take the first name of their address, or each their own name where they are at the root
        _takenRootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StaticAssetsName };
        if (path == "")
            _takenRootNames.UnionWith(PageNames);
        else
            _ = _takenRootNames.Add(path.Split('/')[1]);
    }

    /// <summary>The address of a page of the app (one of <see cref="PageNames"/>).</summary>
    public string Of(string pageName) => PagesPath + "/" + pageName;

    /// <summary>The page that errors are shown on.</summary>
    public string ErrorPage => Of("Error");

    /// <summary>The names the app's addresses take at the root, for the scripts of the pages to tell its addresses from the entries.</summary>
    public IEnumerable<string> TakenRootNames => _takenRootNames;

    /// <summary>Whether the virtual path (without "/" at the start) is in what an address of the app takes: its first name is taken.</summary>
    public bool TakesPath(string virtualPath)
        => _takenRootNames.Contains(virtualPath.Split('/')[0]);

    /// <summary>Whether a new entry can't be at the virtual path, as an address of the app takes its name (see <see cref="Helpers.NameTexts.TakenByTheApp"/>).</summary>
    public bool TakesNewEntry(string newVirtualPath)
        => !newVirtualPath.Contains('/') && TakesPath(newVirtualPath);

    [GeneratedRegex(@"^(/[A-Za-z0-9_-]+)+$")]
    private static partial Regex PathPattern();
}
