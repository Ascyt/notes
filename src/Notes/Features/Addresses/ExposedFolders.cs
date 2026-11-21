using Notes.Features.Bridges;
using Notes.Features.Helpers;

namespace Notes.Features.Addresses;

/// <summary>
/// The directories the site shows (<see cref="Config.Exposed"/>), at their virtual paths: by default all of
/// them. What isn't exposed is no part of the site, for anyone (docs/development/security-specification.md, "Exposed folders"); nor is a
/// name at the root that an address of the app takes (<see cref="AppAddresses"/>). Whether the user may see an exposed entry is still up to
/// the permissions, also of the directories above it.
/// </summary>
public sealed class ExposedFolders
{
    private readonly AppAddresses _app;
    private string[] _roots;

    /// <exception cref="InvalidOperationException">If a directory isn't written as a virtual path, or an address of the app takes it.</exception>
    public ExposedFolders(Config config, AppAddresses app)
    {
        _app = app;
        _roots = [.. config.Exposed.Select(path => path.Trim('/')).Distinct(StringComparer.OrdinalIgnoreCase)];
        foreach (string root in _roots)
        {
            if (root == "" || !root.Split('/').All(PathService.IsValid))
                throw Invalid(root, "it is not a path of a directory (names separated by '/').");
            if (app.TakesPath(root))
                throw Invalid(root, "its name is taken by an address of the app (Config:AppPath).");
        }

        // One inside another adds nothing
        _roots = [.. _roots.Where(root => !_roots.Any(other => other != root && IsSameOrInside(root, other)))];
    }

    /// <summary>Whether the site shows all of the entries.</summary>
    public bool ExposesEverything => _roots.Length == 0;

    /// <summary>
    /// Checks that every exposed directory is there, and takes the spelling it has on disk (names are found ignoring case). Done once when the
    /// app starts, before any request. The app doesn't start without them.
    /// </summary>
    public void CheckOnDisk(IPathService path, EntryLocations locations)
        => _roots = [.. _roots.Select(root => path.ToPhysicalPath(root) is { } physical && Directory.Exists(physical)
            ? locations.ToVirtualPath(physical)
            : throw Invalid(root, "there is no such directory."))];

    /// <summary>Whether the entry (virtual path, "" for the root) is part of the site.</summary>
    public bool IsExposed(string virtualPath)
        => !(virtualPath != "" && _app.TakesPath(virtualPath))
           && (ExposesEverything || _roots.Any(root => IsSameOrInside(virtualPath, root)));

    /// <summary>The exposed directory the (exposed) entry is in, or is: where its breadcrumb starts. "" where everything is exposed.</summary>
    public string RootOf(string virtualPath)
        => _roots.FirstOrDefault(root => IsSameOrInside(virtualPath, root)) ?? "";

    /// <summary>Whether the entry is an exposed directory, or holds one: it has to stay where it is, or the site would lose it.</summary>
    public bool IsOrHoldsExposedFolder(string virtualPath)
        => _roots.Any(root => IsSameOrInside(root, virtualPath));

    private static bool IsSameOrInside(string virtualPath, string directory)
        => directory == ""
           || string.Equals(virtualPath, directory, StringComparison.OrdinalIgnoreCase)
           || virtualPath.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException Invalid(string root, string why)
        => new($"Config:Exposed: '{root}' can't be shown, as {why}");
}
