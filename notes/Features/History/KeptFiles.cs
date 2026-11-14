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
    public bool IsEntryKept(string physicalPath, bool isDirectory)
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

    /// <summary>
    /// Patterns for git's exclude file that leave out what this leaves out, so that git doesn't look through it at all (they are only a
    /// shortcut: what is staged is still decided by <see cref="IsKept(string)"/>). A folder with nothing kept in it or below is left out whole;
    /// in one that is left out but holds something kept (its configuration, or a subfolder kept again), only what is directly in it is left
    /// out, and git looks into what is kept. Git can't take back a pattern for something inside a folder it left out, so each folder git has to
    /// look into is taken back on its own.
    /// </summary>
    /// <remarks>Goes through all folders of the notes (not their files).</remarks>
    public List<string> ExcludePatterns()
        => PatternsInside(paths.Root, "/", NewKnown()) ?? ["/*"];

    /// <summary>The patterns for what is in the folder, or null if nothing in it or below is kept (then it is left out whole).</summary>
    /// <param name="pattern">The folder as a pattern (<c>/media/</c>, <c>/</c> for the root).</param>
    private List<string>? PatternsInside(string folder, string pattern, Dictionary<string, bool> known)
    {
        bool leftOut = IsLeftOut(folder, known);
        bool hasConfig = File.Exists(Path.Combine(folder, PathService.ConfigFileName));
        bool holdsKept = !leftOut || hasConfig;
        List<string> patterns = [];
        if (leftOut)
            patterns.Add(pattern + "*");
        if (leftOut && hasConfig)
            patterns.Add("!" + pattern + EscapeForPattern(PathService.ConfigFileName));

        foreach (string subfolder in SubfoldersOf(folder))
        {
            string name = Path.GetFileName(subfolder);
            // The app's own data is left out by a pattern of its own
            if (paths.IsRoot(folder) && name == PathService.ReservedName)
                continue;

            string subfolderPattern = pattern + EscapeForPattern(name) + "/";
            if (PatternsInside(subfolder, subfolderPattern, known) is not { } inside)
            {
                // (In a folder that is left out, it already is by the folder's pattern)
                if (!leftOut)
                    patterns.Add(subfolderPattern);
                continue;
            }

            holdsKept = true;
            // Taken back from the folder's pattern, so that git looks into it
            if (leftOut)
                patterns.Add("!" + subfolderPattern);
            patterns.AddRange(inside);
        }

        return holdsKept ? patterns : null;
    }

    /// <summary>
    /// The subfolders as git sees them: hidden ones too, links to folders not (git doesn't follow them). In order, so that the exclude file
    /// only changes when what is left out does.
    /// </summary>
    private static IEnumerable<string> SubfoldersOf(string folder)
        => Directory.EnumerateDirectories(folder, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
            .Order(StringComparer.Ordinal);

    /// <summary>A name as it is, not as a pattern: what would be a wildcard (or the escape itself) is escaped.</summary>
    private static string EscapeForPattern(string name)
        => string.Concat(name.Select(character => character is '\\' or '*' or '?' or '[' ? "\\" + character : character.ToString()));

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
