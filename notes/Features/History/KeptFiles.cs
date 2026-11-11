using notes.Features.Configuration;
using notes.Features.Helpers;

namespace notes.Features.History;

/// <summary>
/// What the history keeps: every file, unless the configuration of its folder (or of the nearest folder above that has one) says
/// <c>gitignore</c>. Configuration files are always kept, and the app's own data (<c>_</c>) never is.
/// </summary>
public sealed class KeptFiles(FolderConfigStore configs, HistoryPaths paths)
{
    /// <summary>Whether the file (git path) is kept.</summary>
    public bool IsKept(string path)
        => IsKept(path, NewKnown());

    /// <summary>
    /// Whether the history keeps the entry (physical path): a file as <see cref="IsKept(string)"/> says, a directory unless its configuration
    /// (or the nearest one above) leaves it out. A subdirectory of one that is left out may still be kept, if its own configuration says so.
    /// </summary>
    public bool KeepsEntry(string physicalPath, bool isDirectory)
        => isDirectory
            ? !IsLeftOut(Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath)), NewKnown())
            : IsKept(paths.ToGitPath(physicalPath));

    /// <summary>The same as <see cref="IsKept(string)"/>, for many files at once: each folder is looked at only once.</summary>
    public Func<string, bool> ForManyFiles()
    {
        Dictionary<string, bool> known = NewKnown();
        return path => IsKept(path, known);
    }

    /// <summary>Whether saving the configuration file (git path) with the new contents makes the history leave out its folder, or keep it again.</summary>
    public bool ChangesWhatIsKept(string path, string newContents)
    {
        if (!IsConfigFile(path))
            return false;

        string folder = Path.GetDirectoryName(paths.ToPhysicalPath(path))!;
        string? parent = Path.GetDirectoryName(folder);
        Dictionary<string, bool> known = NewKnown();

        bool leftOutNow = IsLeftOut(folder, known);
        // (Without a valid configuration the folder above decides, as for the one on disk)
        bool leftOutThen = ConfigParser.ParseFolder(newContents, existingFiles: null).Value is { } config
            ? config.Gitignore
            : !paths.IsRoot(folder) && parent is not null && IsLeftOut(parent, known);
        return leftOutNow != leftOutThen;
    }

    private bool IsKept(string path, Dictionary<string, bool> known)
    {
        if (path.Split('/')[0] == PathService.ReservedName)
            return false;

        return IsConfigFile(path) || !IsLeftOut(Path.GetDirectoryName(paths.ToPhysicalPath(path))!, known);
    }

    /// <summary>
    /// What the nearest folder with a valid configuration says (an invalid one says nothing here: leaving something out of the history is never
    /// guessed). Nothing is left out without any.
    /// </summary>
    private bool IsLeftOut(string folder, Dictionary<string, bool> known)
    {
        if (known.TryGetValue(folder, out bool leftOut))
            return leftOut;

        string? parent = Path.GetDirectoryName(folder);
        leftOut = configs.Get(folder).Config is { } config ? config.Gitignore
            : parent is not null && !paths.IsRoot(folder) && IsLeftOut(parent, known);
        known[folder] = leftOut;
        return leftOut;
    }

    private static bool IsConfigFile(string path)
        => string.Equals(Path.GetFileName(path), PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, bool> NewKnown()
        => new(StringComparer.OrdinalIgnoreCase);
}
