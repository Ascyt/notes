using Notes.Features.Helpers;

namespace Notes.Features.Bridges;

/// <summary>A physical directory that holds part of the entries: the root (<c>Config:Dir</c>), or a bridge.</summary>
/// <param name="VirtualPath">Where it is among the entries: <c>""</c> for the root, otherwise like <c>hdd/archive</c>, spelled as configured.</param>
/// <param name="PhysicalPath">Its physical directory (full, without a separator at the end).</param>
/// <param name="MountPath">
/// For a bridge, the physical path it would have in the directory above it if it weren't a bridge. Whatever is there on disk is hidden by
/// the bridge. Null for the root.
/// </param>
/// <param name="KeepsHistory">Whether it has a history of its own (<c>History:Enabled</c>).</param>
public sealed record EntryLocation(string VirtualPath, string PhysicalPath, string? MountPath, bool KeepsHistory)
{
    public bool IsRoot => MountPath is null;

    /// <summary>The virtual name of the directory (for a bridge, not that of its physical directory).</summary>
    public string Name => VirtualPath[(VirtualPath.LastIndexOf('/') + 1)..];

    /// <summary>The folder for the app's own data of this location (temporary files, the history), which is never part of the entries.</summary>
    public string DataDirectory => Path.Combine(PhysicalPath, PathService.ReservedName);
}

/// <summary>A file or directory inside a directory, with the name it has there (a bridge's differs from that of its physical directory).</summary>
public readonly record struct ChildEntry(FileSystemInfo Entry, string Name);

/// <summary>
/// Where the entries are on disk: the root (<c>Config:Dir</c>) and the bridges (<c>Config:Bridges</c>), each a virtual directory kept in a physical
/// directory of its own (see docs/administration/bridges.md). Physical paths are what the app works with, so everything that goes from an
/// entry to the directory it is in, or to what is in a directory, asks here instead of looking at the physical directories: a bridge's
/// physical directory is not inside the one it is in as far as the app is concerned.
/// </summary>
/// <remarks>
/// The physical directories never overlap, so every physical path belongs to at most one location. A bridge hides whatever is on disk where
/// it is mounted (<see cref="EntryLocation.MountPath"/>), so that a virtual path always means one thing.
/// </remarks>
public sealed class EntryLocations
{
    public EntryLocations(Config config)
    {
        Root = new EntryLocation("", Normalize(config.Dir), MountPath: null, config.History.Enabled);

        // The bridges above first, so that a bridge inside another is mounted in the physical directory of that one
        List<EntryLocation> all = [Root];
        foreach ((BridgeConfig bridge, string virtualPath) in config.Bridges.Select(bridge => (bridge, ValidVirtualPath(bridge))).OrderBy(pair
            => SegmentsOf(pair.Item2).Length))
        {
            if (string.IsNullOrWhiteSpace(bridge.Physical) || !Path.IsPathFullyQualified(bridge.Physical))
                throw Invalid(bridge.Virtual, "its physical directory must be a full path");
            string physicalPath = Normalize(bridge.Physical);

            if (all.Any(other => string.Equals(other.VirtualPath, virtualPath, StringComparison.OrdinalIgnoreCase)))
                throw Invalid(bridge.Virtual, "another bridge has the same virtual path");
            // Two virtual paths for the same files would give them two sets of permissions
            if (all.FirstOrDefault(other => IsSameOrInsidePhysical(physicalPath, other.PhysicalPath)
                || IsSameOrInsidePhysical(other.PhysicalPath, physicalPath)) is { } overlapping)
                throw Invalid(bridge.Virtual,
                    $"its physical directory overlaps that of {(overlapping.IsRoot ? "the entries (Config:Dir)" : "the bridge /" + overlapping.VirtualPath)}");

            EntryLocation above = DeepestLocationOf(all, ParentOfVirtual(virtualPath));
            string mountPath = Path.Combine([above.PhysicalPath, .. SegmentsOf(virtualPath).Skip(SegmentsOf(above.VirtualPath).Length)]);
            all.Add(new EntryLocation(virtualPath, physicalPath, mountPath, bridge.History.Enabled));
        }

        All = all;
        Bridges = [.. all.Skip(1)];
    }

    public EntryLocation Root { get; }

    /// <summary>The root and the bridges, each bridge after the one it is in.</summary>
    public IReadOnlyList<EntryLocation> All { get; }

    public IReadOnlyList<EntryLocation> Bridges { get; }

    /// <summary>
    /// Checks on disk what the configuration can't say by itself, once the root exists: every bridge's physical directory exists (and is no link,
    /// whose target could change), and so does the directory it is mounted in, as the app finds it (<paramref name="findDirectory"/>, given a
    /// virtual path). Throws an <see cref="InvalidOperationException"/> otherwise. What a bridge hides on disk is logged.
    /// </summary>
    public void CheckOnDisk(Func<string, string?> findDirectory, ILogger logger)
    {
        foreach (EntryLocation bridge in Bridges)
        {
            DirectoryInfo physical = new(bridge.PhysicalPath);
            if (!physical.Exists)
                throw Invalid(bridge.VirtualPath, "its physical directory does not exist");
            if (physical.LinkTarget is not null)
                throw Invalid(bridge.VirtualPath, "its physical directory is a link (give the directory it leads to instead)");

            string parentVirtual = ParentOfVirtual(bridge.VirtualPath);
            string? parent = findDirectory(parentVirtual);
            if (parent is null || !Directory.Exists(parent) || !IsSamePhysical(parent, Path.GetDirectoryName(bridge.MountPath)!))
                throw Invalid(bridge.VirtualPath, $"the directory it is in (/{parentVirtual}) does not exist, or is spelled differently on disk");

            if (Path.Exists(bridge.MountPath))
                logger.LogWarning("The bridge /{bridge} hides {hidden}, which is there on disk where it is mounted", bridge.VirtualPath,
                    bridge.MountPath);
        }
    }

    /// <summary>The location whose physical directory the path is in (or is), or null if it is outside of all of them.</summary>
    public EntryLocation? LocationOf(string physicalPath)
    {
        string path = Normalize(physicalPath);
        // The bridges first: those inside another are listed after it
        return All.Reverse().FirstOrDefault(location => IsSameOrInsidePhysical(path, location.PhysicalPath));
    }

    /// <summary>The location the virtual path is in: the deepest bridge it is at or below, or the root.</summary>
    public EntryLocation LocationOfVirtual(string virtualPath)
        => DeepestLocationOf(All, string.Join('/', SegmentsOf(virtualPath)));

    /// <summary>The bridge at exactly the virtual path (in any case), or null.</summary>
    public EntryLocation? BridgeAt(string virtualPath)
    {
        string path = string.Join('/', SegmentsOf(virtualPath));
        return Bridges.FirstOrDefault(bridge => string.Equals(bridge.VirtualPath, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The bridge whose physical directory this is, or null.</summary>
    public EntryLocation? BridgeOf(string physicalDirectory)
    {
        string path = Normalize(physicalDirectory);
        return Bridges.FirstOrDefault(bridge => IsSamePhysical(path, bridge.PhysicalPath));
    }

    /// <summary>Whether the path is where a bridge is mounted, or inside it: whatever is there on disk is hidden by the bridge.</summary>
    public bool IsHiddenByBridge(string physicalPath)
    {
        string path = Normalize(physicalPath);
        return Bridges.Any(bridge => IsSameOrInsidePhysical(path, bridge.MountPath!));
    }

    /// <summary>Whether the path (that may not exist) is part of the entries: in one of the locations, and not hidden by a bridge.</summary>
    public bool Contains(string physicalPath)
        => LocationOf(physicalPath) is not null && !IsHiddenByBridge(physicalPath);

    /// <summary>The virtual path of the physical one, with <c>/</c> and without one at the start; <c>""</c> for the root.</summary>
    public string ToVirtualPath(string physicalPath)
    {
        string path = Normalize(physicalPath);
        EntryLocation location = LocationOf(path) ?? Root;
        string relative = Path.GetRelativePath(location.PhysicalPath, path).Replace('\\', '/');
        if (relative == ".")
            return location.VirtualPath;
        return location.IsRoot ? relative : location.VirtualPath + "/" + relative;
    }

    /// <summary>The directory the entry is in as far as the app is concerned (for a bridge, where it is mounted); null for the root.</summary>
    public string? ParentOf(string physicalPath)
    {
        string path = Normalize(physicalPath);
        if (BridgeOf(path) is { } bridge)
            return Path.GetDirectoryName(bridge.MountPath);
        return IsSamePhysical(path, Root.PhysicalPath) ? null : Path.GetDirectoryName(path);
    }

    /// <summary>The virtual name of the entry (for a bridge, not that of its physical directory).</summary>
    public string NameOf(string physicalPath)
        => BridgeOf(physicalPath)?.Name ?? Path.GetFileName(Normalize(physicalPath));

    /// <summary>The bridges mounted right inside the physical directory.</summary>
    public IEnumerable<EntryLocation> BridgesIn(string physicalDirectory)
    {
        string path = Normalize(physicalDirectory);
        return Bridges.Where(bridge => IsSamePhysical(Path.GetDirectoryName(bridge.MountPath)!, path));
    }

    /// <summary>
    /// Whether the entry is a bridge or has one somewhere inside it. It can't be moved, renamed or deleted then: the configuration puts the
    /// bridge where it is.
    /// </summary>
    public bool HoldsBridge(string physicalPath)
        => BridgesAtOrBelow(physicalPath).Any();

    /// <summary>The bridges at the entry or somewhere inside it, the ones above first.</summary>
    public IEnumerable<EntryLocation> BridgesAtOrBelow(string physicalPath)
    {
        string virtualPath = ToVirtualPath(physicalPath);
        return Bridges.Where(bridge => IsSameOrInsideVirtual(bridge.VirtualPath, virtualPath));
    }

    /// <summary>
    /// Whether <paramref name="inner"/> is <paramref name="outer"/> or somewhere inside it, as far as the app is concerned (also across
    /// bridges).
    /// </summary>
    public bool IsSameOrInside(string inner, string outer)
        => IsSameOrInsideVirtual(ToVirtualPath(inner), ToVirtualPath(outer));

    /// <summary>
    /// Whether a new entry may be put at the path: nothing is there on disk under that name in any case, and it is not where a bridge is mounted.
    /// Names are the same in any case throughout the entries (paths are found ignoring it, and so are the entries of the configuration), also on a
    /// file system that tells cases apart: there, "Secret.md" next to "secret.md" would share its permissions, and deleting it would take them away.
    /// </summary>
    public bool IsFree(string physicalPath)
        => !Path.Exists(physicalPath) && !IsHiddenByBridge(physicalPath) && !HasEntryNamedInAnyCase(Path.GetDirectoryName(physicalPath)!,
            Path.GetFileName(physicalPath));

    /// <summary>Whether the directory has an entry of the name, in any case. If it can't be told, it counts as taken.</summary>
    private static bool HasEntryNamedInAnyCase(string directory, string name)
    {
        bool Matches(string pattern)
        {
            EnumerationOptions options = new() { AttributesToSkip = 0, IgnoreInaccessible = false, MatchType = MatchType.Simple,
                MatchCasing = MatchCasing.CaseInsensitive };
            return new DirectoryInfo(directory).EnumerateFileSystemInfos(pattern, options).Any(entry => string.Equals(entry.Name, name,
                StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            // The name as a pattern (one with * or ? in it matches more than itself, hence the comparison); everything for a name the
            // enumeration refuses as a pattern
            try
            {
                return Matches(name);
            }
            catch (ArgumentException)
            {
                return Matches("*");
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing can be put there, which whoever tries finds out
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// The folder for temporary files of the <paramref name="kind"/> in the location the path is in, so that they are on the same volume as
    /// what they become (putting them in place is a rename then, which happens at once).
    /// </summary>
    public string TempDirectoryFor(string physicalPath, string kind)
        => Path.Combine((LocationOf(physicalPath) ?? Root).DataDirectory, "temp", kind);

    /// <summary>
    /// What is in the directory as far as the app is concerned: what is on disk, without what a bridge hides, and the bridges mounted in it.
    /// Throws as <see cref="DirectoryInfo.EnumerateFileSystemInfos(string, EnumerationOptions)"/> does.
    /// </summary>
    public IEnumerable<ChildEntry> ListDirectory(DirectoryInfo directory, EnumerationOptions options)
    {
        List<EntryLocation> mounted = [.. BridgesIn(directory.FullName)];
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos("*", options))
        {
            if (!mounted.Any(bridge => IsSamePhysical(entry.FullName, bridge.MountPath!)))
                yield return new ChildEntry(entry, entry.Name);
        }

        foreach (EntryLocation bridge in mounted)
            yield return new ChildEntry(new DirectoryInfo(bridge.PhysicalPath), bridge.Name);
    }

    private static EntryLocation DeepestLocationOf(IEnumerable<EntryLocation> locations, string virtualPath)
        => locations.Where(location => IsSameOrInsideVirtual(virtualPath, location.VirtualPath))
            .MaxBy(location => SegmentsOf(location.VirtualPath).Length)!;

    private static string ValidVirtualPath(BridgeConfig bridge)
    {
        string[] names = SegmentsOf(bridge.Virtual ?? "");
        if (names.Length == 0)
            throw Invalid(bridge.Virtual, "the root can't be a bridge");
        if (!names.All(PathService.IsValid))
            throw Invalid(bridge.Virtual, "its virtual path has a name that no entry can have");
        return string.Join('/', names);
    }

    private static InvalidOperationException Invalid(string? virtualPath, string why)
        => new($"Config:Bridges: the bridge '{virtualPath}' is not valid: {why}.");

    private static string Normalize(string physicalPath)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));

    private static string[] SegmentsOf(string virtualPath)
        => virtualPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static string ParentOfVirtual(string virtualPath)
        => virtualPath.LastIndexOf('/') is var slash and >= 0 ? virtualPath[..slash] : "";

    private static bool IsSameOrInsideVirtual(string inner, string outer)
        => outer == "" || string.Equals(inner, outer, StringComparison.OrdinalIgnoreCase) || inner.StartsWith(outer + "/",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSamePhysical(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInsidePhysical(string inner, string outer)
    {
        string normalizedOuter = Normalize(outer);
        return IsSamePhysical(inner, normalizedOuter) || Normalize(inner).StartsWith(Path.TrimEndingDirectorySeparator(normalizedOuter)
            + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
