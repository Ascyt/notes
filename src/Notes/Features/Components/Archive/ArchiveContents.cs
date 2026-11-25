using System.Collections.Concurrent;
using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Components.Download;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Archive;

/// <summary>
/// What goes into an archive for a user, and the check of the permissions once it is written. Entries the user may not read are left out,
/// with what is in them, like a zip of a directory does; each is checked when the writer gets to it. Every entry that went in is noted, so
/// that <see cref="StillReadable"/> can check them all again at the end: an archive that took long may otherwise hold what the user was no
/// longer allowed to read while it was made.
/// </summary>
public sealed class ArchiveContents
{
    private readonly ClaimsPrincipal _user;
    private readonly AccessTree _tree;

    /// <summary>The selected entries (or the directory) at the top, which must still be openable at the end.</summary>
    private readonly List<string> _topLevel = [];

    /// <summary>Everything inside them that went in (the writer may list from several threads, so it is safe for that).</summary>
    private readonly ConcurrentBag<string> _inside = [];

    private int _leftOutUnreadable;

    /// <summary>The archived folders that go in as they are kept (<see cref="KeepAsIs"/>), by the physical path of each folder.</summary>
    private HashSet<string> _keptAsIs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The archived folder whose unzipped copy a path is, or the path itself (<see cref="ZippedFolders.ArchivedFolders.OuterOf"/>).</summary>
    private Func<string, string> _outerOf = path => path;

    private ArchiveContents(ClaimsPrincipal user, AccessTree tree, Func<ArchiveContents, ZipFolderEntry> makeFolder)
    {
        _user = user;
        _tree = tree;
        Folder = makeFolder(this);
    }

    /// <summary>
    /// The archived folders (physical paths of the folders) that go in as they are kept: their configuration and their archive, instead of
    /// what is in them (for admins, see <see cref="ArchiveHandler"/>). They are to be zipped before the archive is written. Set before it is
    /// written: what goes in is only listed then.
    /// </summary>
    public void KeepAsIs(IEnumerable<string> folders, Func<string, string> outerOf)
    {
        _keptAsIs = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        _outerOf = outerOf;
    }

    /// <summary>
    /// What an archived folder kept as it is goes in as (under its name): its <c>.config.json</c> and its archive, as they are on disk, so that
    /// putting it back makes it the archived folder it was. Null for any other directory.
    /// </summary>
    private ZipFolderEntry? AsKept(DirectoryInfo directory, string name)
    {
        string folder = _outerOf(directory.FullName);
        if (!_keptAsIs.Contains(folder))
            return null;
        return new ZipFolderEntry(name, () =>
        {
            List<ZipEntry> kept = [];
            foreach (string? file in new[] { Path.Combine(folder, PathService.ConfigFileName), ZippedFolders.ArchivedFolders.ArchiveFileOf(folder) })
                if (file is not null && File.Exists(file))
                    kept.Add(new ZipFileEntry(Path.GetFileName(file), File.GetLastWriteTime(file), () => OpenForReading(file)));
            return kept;
        });
    }

    /// <summary>What is written into the archive, listed as the writer gets to it.</summary>
    public ZipFolderEntry Folder { get; }

    /// <summary>Whether entries the user may not read were left out (known once the archive is written).</summary>
    public bool LeftOutUnreadable => _leftOutUnreadable != 0;

    /// <summary>What is in the directory (which the user may open), as the archive's top level.</summary>
    public static ArchiveContents OfDirectory(ClaimsPrincipal user, AccessTree tree, EntryLocations locations, string physicalDirectory)
        => new(user, tree, contents =>
        {
            contents._topLevel.Add(physicalDirectory);
            return ZipService.FolderOnDisk(locations, physicalDirectory, contents.Include, contents.AsKept);
        });

    /// <summary>
    /// The entries (which the user may open), each at the archive's top level by its name. Where two have the same name (selected in different
    /// directories), the later ones get a number, as "name (2).ext".
    /// </summary>
    public static ArchiveContents OfEntries(ClaimsPrincipal user, AccessTree tree, EntryLocations locations, IReadOnlyList<FileSystemInfo> entries)
        => new(user, tree, contents =>
        {
            contents._topLevel.AddRange(entries.Select(entry => entry.FullName));
            HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
            List<ZipEntry> topLevel = [];
            foreach (FileSystemInfo entry in entries)
            {
                string name = FreeName(locations.NameOf(entry.FullName), entry is DirectoryInfo, taken);
                if (entry is DirectoryInfo directory)
                {
                    // (Whether it goes in as it is kept is only known once KeepAsIs was told, before the archive is written)
                    ZipFolderEntry onDisk = ZipService.FolderOnDisk(locations, directory.FullName, contents.Include, contents.AsKept) with { Name = name };
                    topLevel.Add(new ZipFolderEntry(name, () => (contents.AsKept(directory, name) ?? onDisk).ListContents()));
                }
                else
                    topLevel.Add(new ZipFileEntry(name, entry.LastWriteTime, () => OpenForReading(entry.FullName)));
            }

            return new ZipFolderEntry("", () => topLevel);
        });

    /// <summary>
    /// Whether the user may still read everything that went in, now that it is written. Changes made while it was written that took away the
    /// permission to read anything in it make the archive one the user must not get.
    /// </summary>
    public bool StillReadable()
        => _topLevel.All(path => _tree.CanOpen(_user, path)) && _inside.All(path => _tree.GetAccess(_user, path).CanRead);

    /// <summary>Asked by the writer for each entry inside a directory as it gets to it.</summary>
    private bool Include(FileSystemInfo entry)
    {
        if (!_tree.GetAccess(_user, entry.FullName).CanRead)
        {
            _ = Interlocked.Exchange(ref _leftOutUnreadable, 1);
            return false;
        }

        _inside.Add(entry.FullName);
        return true;
    }

    /// <summary>The name, or with " (2)", " (3)", … before its extension (a directory has none) until it is one not taken yet.</summary>
    private static string FreeName(string name, bool isDirectory, HashSet<string> taken)
    {
        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? "" : Path.GetExtension(name);
        string free = name;
        for (int number = 2; !taken.Add(free); number++)
            free = $"{stem} ({number}){extension}";
        return free;
    }

    // As the zip of a directory reads its files: one being edited at the moment is still read
    private static FileStream OpenForReading(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
}
