namespace Notes.Features.ZippedFolders;

/// <summary>
/// What a folder holds, to compare an archive with what went into it: every file by its path (with "/" between its parts) with the SHA-256
/// of its contents, and every directory, also those only known as the directory of a file.
/// </summary>
internal sealed class FolderManifest
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public int FileCount => _files.Count;

    /// <summary>Notes a file. False if one of that path is noted already (an archive that has it twice is damaged).</summary>
    public bool AddFile(string path, byte[] hash)
    {
        lock (_lock)
        {
            if (!_files.TryAdd(path, hash))
                return false;
            AddParents(path);
            return true;
        }
    }

    public void AddDirectory(string path)
    {
        lock (_lock)
        {
            _ = _directories.Add(path);
            AddParents(path);
        }
    }

    /// <summary>Whether both hold the same files with the same contents, and the same directories.</summary>
    public bool SameAs(FolderManifest other)
        => _files.Count == other._files.Count
           && _files.All(file => other._files.TryGetValue(file.Key, out byte[]? hash) && hash.AsSpan().SequenceEqual(file.Value))
           && _directories.SetEquals(other._directories);

    private void AddParents(string path)
    {
        for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            if (!_directories.Add(path[..slash]))
                return;
    }
}
