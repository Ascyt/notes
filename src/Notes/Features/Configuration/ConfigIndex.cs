using System.Collections.Concurrent;
using Notes.Features.Bridges;
using Notes.Features.Helpers;

namespace Notes.Features.Configuration;

/// <summary>
/// Which folders have a configuration file, so that finding out needs no look at the disk (principles.md, 22): most folders have none. Each
/// location (the root, a bridge, an unzipped archived folder) is walked once (<see cref="Walk"/>: when the app starts, when it is mounted), and
/// the index is kept up to date by what the app changes (<see cref="FolderConfigStore"/>: the files it writes, the trees that operations move,
/// copy, delete, mount or unmount). A file added or deleted on the server by hand counts once <see cref="FolderConfigStore.Rescan"/> (a
/// background job) found it. A location that wasn't walked, or couldn't be, is looked at on disk as before.
/// </summary>
public sealed class ConfigIndex(EntryLocations locations)
{
    /// <summary>The directories (physical, normalized) that have a configuration file.</summary>
    private readonly ConcurrentDictionary<string, byte> _withConfig = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The locations (by physical path) that were walked completely: only for them the index is the truth.</summary>
    private readonly ConcurrentDictionary<string, byte> _walked = new(StringComparer.OrdinalIgnoreCase);

    private long _changes;

    /// <summary>How often the index was changed, so that a rescan knows whether what it found is still all there is to it.</summary>
    public long Changes => Interlocked.Read(ref _changes);

    /// <summary>Whether the index knows about the directory (physical, normalized): it is in a location that was walked.</summary>
    public bool Knows(string directory)
        => locations.LocationOf(directory) is { } location && _walked.ContainsKey(location.PhysicalPath);

    /// <summary>Whether the directory has a configuration file, as far as the index knows (see <see cref="Knows"/>).</summary>
    public bool Has(string directory) => _withConfig.ContainsKey(directory);

    public void Add(string directory)
    {
        _withConfig[directory] = 0;
        Changed();
    }

    /// <summary>Forgets what it knows of the path and everything below it.</summary>
    public void Forget(string path)
    {
        foreach (string directory in _withConfig.Keys)
            if (IsSameOrInside(directory, path))
                _ = _withConfig.TryRemove(directory, out _);
        Changed();
    }

    /// <summary>What it knows of the path and below it is now of <paramref name="to"/> (renamed or moved, nothing else changed).</summary>
    public void Move(string from, string to)
    {
        foreach (string directory in _withConfig.Keys)
            if (IsSameOrInside(directory, from) && _withConfig.TryRemove(directory, out _))
                _withConfig[to + directory[from.Length..]] = 0;
        Changed();
    }

    /// <summary>
    /// Takes in a walk of the path (<see cref="Walk"/>): what was known below it is replaced by what was found. Only what is of the path's
    /// location: another location whose directory is inside it (an unzipped archived folder, in the root's data) was not walked.
    /// </summary>
    public void Replace(string path, IEnumerable<string> found)
    {
        EntryLocation? location = locations.LocationOf(path);
        foreach (string directory in _withConfig.Keys)
            if (IsSameOrInside(directory, path) && locations.LocationOf(directory) == location)
                _ = _withConfig.TryRemove(directory, out _);
        foreach (string directory in found)
            _withConfig[directory] = 0;
        Changed();
    }

    /// <summary>The location (physical path) was walked completely (<see cref="Replace"/>), or is gone (unmounted) or couldn't be walked.</summary>
    public void SetWalked(string location, bool walked)
    {
        if (walked)
            _walked[location] = 0;
        else
            _ = _walked.TryRemove(location, out _);
        Changed();
    }

    /// <summary>
    /// The configuration files at and below the directory (physical paths of the files), each directory listed once; links aren't followed.
    /// A location's own data (<c>_</c> in its root) is left out. Throws if a directory can't be read: then what the index would say about it is
    /// unknown.
    /// </summary>
    public static List<FileInfo> Walk(string directory, bool isLocationRoot)
    {
        List<FileInfo> found = [];
        EnumerationOptions options = new() { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        Stack<(DirectoryInfo Directory, bool IsLocationRoot)> pending = new([(new DirectoryInfo(directory), isLocationRoot)]);
        while (pending.TryPop(out (DirectoryInfo Directory, bool IsLocationRoot) next))
            foreach (FileSystemInfo entry in next.Directory.EnumerateFileSystemInfos("*", options))
            {
                if (entry is DirectoryInfo inner)
                {
                    if (!(next.IsLocationRoot && entry.Name == PathService.ReservedName))
                        pending.Push((inner, false));
                }
                else if (string.Equals(entry.Name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase))
                    found.Add((FileInfo)entry);
            }
        return found;
    }

    /// <summary>The directories that have a configuration file in the location, as the index has them (for a rescan to compare).</summary>
    public List<string> In(EntryLocation location)
        => [.. _withConfig.Keys.Where(directory => locations.LocationOf(directory) == location)];

    private void Changed() => Interlocked.Increment(ref _changes);

    private static bool IsSameOrInside(string path, string folder)
        => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
