using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.Helpers;

namespace Notes.Features.ZippedFolders;

/// <summary>What <see cref="ArchiveSettingsResolver.Resolve"/> found.</summary>
/// <param name="Settings">The settings the folder is archived with now, or null if it isn't archived.</param>
/// <param name="IsCertain">
/// False if a configuration that decides it isn't valid: then it can't be told, and nothing may be done about it (above all, archiving is never
/// taken as turned off).
/// </param>
public readonly record struct ResolvedArchive(ArchiveSettings? Settings, bool IsCertain);

/// <summary>
/// Whether a folder is archived, and how, from the configurations: its own <c>archive</c> (enabled, and not <c>onLowestLevel</c>), or, if it
/// sets none (or null), that of the nearest folder above that sets one, if that one says <c>onLowestLevel</c>. Below such a folder, a folder is
/// archived with its settings if it holds files and no folder between them does (the lowest level), or if it is archived on disk already: once
/// archived, it stays so (even after a folder above gained files, which is then archived around it). <c>{ "enabled": false }</c> on a folder
/// between cuts it off. The search doesn't go above a bridge, nor reach into one from above.
/// </summary>
public sealed class ArchiveSettingsResolver(FolderConfigStore configs, EntryLocations locations)
{
    /// <summary>The settings the folder (its physical path, or that of its unzipped copy) is archived with now, or null.</summary>
    public ArchiveSettings? SettingsOf(string physicalDirectory)
        => Resolve(physicalDirectory).Settings;

    public ResolvedArchive Resolve(string physicalDirectory)
    {
        string folder = OuterOf(physicalDirectory);
        FolderConfigState own = configs.Get(folder);
        if (own.IsInvalid)
            return new ResolvedArchive(null, IsCertain: false);
        if (own.Config?.OwnArchive is { } set)
            return new ResolvedArchive(set.Enabled && !set.OnLowestLevel ? set : null, IsCertain: true);

        // The nearest folder above that says something; the ones on the way don't
        ArchiveSettings? lowestLevel = null;
        List<string> between = [];
        for (string? up = IsLocationRoot(folder) ? null : locations.ParentOf(folder); up is not null;
             up = IsLocationRoot(up) ? null : locations.ParentOf(up))
        {
            FolderConfigState state = configs.Get(up);
            if (state.IsInvalid)
                return new ResolvedArchive(null, IsCertain: false);
            if (state.Config?.OwnArchive is { } above)
            {
                if (above.Enabled && above.OnLowestLevel)
                    lowestLevel = above;
                break;
            }
            between.Add(up);
        }
        if (lowestLevel is null)
            return new ResolvedArchive(null, IsCertain: true);

        ArchiveSettings inherited = lowestLevel with { OnLowestLevel = false };
        if (IsArchivedOnDisk(folder))
            return new ResolvedArchive(inherited, IsCertain: true);
        // The lowest level: it holds files, and none of the folders on the way is archived (it would hold this one) or holds files
        bool isLowest = HoldsFiles(folder) && !between.Any(above => IsArchivedOnDisk(OuterOf(above)) || HoldsFiles(above));
        return new ResolvedArchive(isLowest ? inherited : null, IsCertain: true);
    }

    /// <summary>
    /// Whether an <c>onLowestLevel</c> folder decides for the directory (physical): the nearest folder at or above it that sets <c>archive</c>
    /// is one. Only then can a change in it (a new file) make it, or a folder in it, one to archive.
    /// </summary>
    public bool IsBelowLowestLevel(string physicalDirectory)
    {
        for (string? up = OuterOf(physicalDirectory); up is not null; up = IsLocationRoot(up) ? null : locations.ParentOf(up))
            if (configs.Get(up).Config?.OwnArchive is { } set)
                return set is { Enabled: true, OnLowestLevel: true };
        return false;
    }

    /// <summary>
    /// Whether the folder (its physical path) is archived on disk now: its unzipped copy is mounted, or it has an archive (whose name is
    /// reserved: no user's file has it), or an unzipped copy (before it is mounted at the start, or before it is first zipped).
    /// </summary>
    public bool IsArchivedOnDisk(string folderPath)
        // (The root and a bridge never are: "their copy" would be the folder of all copies. A directory of the copies that is only on the way
        // to those of folders inside this one isn't its copy: this one then holds those folders)
        => !IsLocationRoot(folderPath)
           && (locations.UnzippedArchives.Any(archive => string.Equals(archive.MountPath, Normalize(folderPath), StringComparison.OrdinalIgnoreCase))
               || ArchivedFolder.ArchivePathsIn(folderPath).Any(File.Exists)
               || (Directory.Exists(UnzippedDirectoryOf(folderPath)) && HoldsOnlyItsOwn(folderPath)));

    /// <summary>
    /// Whether the folder holds nothing but its configuration and archive: what is left of an archived folder whose contents are its copy.
    /// </summary>
    public static bool HoldsOnlyItsOwn(string folderPath)
        => Directory.EnumerateFileSystemEntries(folderPath).Select(Path.GetFileName).All(name =>
            string.Equals(name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase) || ArchivedFolder.IsArchiveFileName(name!));

    /// <summary>Where the folder's unzipped copy is (whether or not it is there): in the data of the location it is in.</summary>
    public string UnzippedDirectoryOf(string folderPath)
    {
        string folder = Normalize(folderPath);
        EntryLocation location = locations.LocationOf(folder) ?? locations.Root;
        return Path.Combine(location.DataDirectory, "temp", ArchivedFolders.UnzippedKind, Path.GetRelativePath(location.PhysicalPath, folder));
    }

    /// <summary>Whether the directory (physical) holds a file of its own, other than its configuration (and an archive).</summary>
    public static bool HoldsFiles(string physicalDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(physicalDirectory, "*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true })
                .Select(Path.GetFileName)
                .Any(name => !string.Equals(name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase)
                             && !ArchivedFolder.IsArchiveFileName(name!));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The archived folder whose unzipped copy the path is, or the path itself.</summary>
    private string OuterOf(string physicalPath)
        => locations.BridgeOf(physicalPath) is { IsArchive: true } archive ? archive.MountPath! : Normalize(physicalPath);

    /// <summary>Whether it is the root or a bridge (not an unzipped archived folder, which stands for its folder).</summary>
    public bool IsLocationRoot(string physicalPath)
        => locations.All.Any(location => !location.IsArchive
                                         && string.Equals(location.PhysicalPath, Normalize(physicalPath), StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
