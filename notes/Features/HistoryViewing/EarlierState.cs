using System.Text;
using LibGit2Sharp;
using notes.Features.Configuration;
using notes.Features.Helpers;
using notes.Features.History;

namespace notes.Features.HistoryViewing;

/// <summary>An entry of an earlier state.</summary>
/// <param name="GitPath">Its path relative to the root of the notes, with "/" ("" for the root), spelled as the history has it.</param>
/// <param name="Target">The directory's tree or the file's blob in the repository.</param>
public sealed record EarlierEntry(string GitPath, bool IsDirectory, GitObject Target)
{
    public string Name => GitPath.Split('/')[^1];

    /// <summary>The file's contents, null for a directory.</summary>
    public Blob? Blob => Target as Blob;
}

/// <summary>
/// The notes as one commit of the history has them (docs/development/history-specification.md): their entries, and the configuration that was
/// in force, for <see cref="Auth.IEntryAccess"/> to decide by. Names are looked up as the notes do, without case. The configuration files and
/// the app's own data are no entries of it.
/// </summary>
public sealed class EarlierState : IConfigSnapshot, IDisposable
{
    private readonly Repository _repository;
    private readonly bool _ownsRepository;
    private readonly Commit _commit;
    private readonly HistoryPaths _paths;
    private readonly Func<Blob, FolderConfigState> _readConfigurationFile;

    /// <summary>What <see cref="FindEntry"/> found for each path asked for: the permissions ask for the same paths again and again.</summary>
    private readonly Dictionary<string, EarlierEntry?> _foundEntries = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="ownsRepository">Whether the repository is disposed with the state (otherwise whoever opened it disposes it).</param>
    /// <param name="readConfigurationFile">What a configuration file of the history says.</param>
    internal EarlierState(Repository repository, bool ownsRepository, Commit commit, HistoryPaths paths, Func<Blob, FolderConfigState> readConfigurationFile)
    {
        _repository = repository;
        _ownsRepository = ownsRepository;
        _commit = commit;
        _paths = paths;
        _readConfigurationFile = readConfigurationFile;
    }

    /// <summary>When the state was committed. It is the state for every time after this, up to the next commit.</summary>
    public DateTimeOffset CommitTime => _commit.Author.When;

    public string ToGitPath(string physicalPath) => _paths.ToGitPath(physicalPath);

    public string ToPhysicalPath(string gitPath) => _paths.ToPhysicalPath(gitPath);

    /// <summary>The entry at the path (git path, <c>""</c> for the root), or null if there was none.</summary>
    public EarlierEntry? FindEntry(string gitPath)
    {
        if (_foundEntries.TryGetValue(gitPath, out EarlierEntry? known))
            return known;

        // Down from the root, one name at a time, as an address of the notes is resolved
        EarlierEntry? current = new("", IsDirectory: true, _commit.Tree);
        foreach (string name in gitPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            // Only a directory has anything in it: a name below a file finds nothing
            if (current.Target is not Tree directory || FindChild(directory, name, isRoot: current.GitPath == "") is not { } child)
            {
                current = null;
                break;
            }

            // The path is made of the names as the history spells them, which may differ in case from what was asked for
            string childPath = current.GitPath == "" ? child.Name : current.GitPath + "/" + child.Name;
            current = new EarlierEntry(childPath, child.TargetType == TreeEntryTargetType.Tree, child.Target);
        }

        return _foundEntries[gitPath] = current;
    }

    /// <summary>What was in the directory, without what is no entry of the notes (see <see cref="IsEntryOfTheNotes"/>).</summary>
    public IEnumerable<EarlierEntry> ListDirectory(EarlierEntry directory)
    {
        if (directory.Target is not Tree tree)
            yield break;

        foreach (TreeEntry child in tree.Where(child => IsEntryOfTheNotes(child, isRoot: directory.GitPath == "")))
        {
            string childPath = directory.GitPath == "" ? child.Name : directory.GitPath + "/" + child.Name;
            yield return new EarlierEntry(childPath, child.TargetType == TreeEntryTargetType.Tree, child.Target);
        }
    }

    /// <summary>The text of a file of a state, as the editor reads text (UTF-8, a byte order mark is looked past).</summary>
    public static string ReadText(Blob blob)
    {
        using StreamReader reader = new(blob.GetContentStream(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public FolderConfigState FolderConfigOf(string physicalDirectory)
    {
        if (FindEntry(_paths.ToGitPath(physicalDirectory)) is not { Target: Tree directory })
            return FolderConfigState.Missing;

        return ConfigurationFileIn(directory) is { } file ? _readConfigurationFile((Blob)file.Target) : FolderConfigState.Missing;
    }

    public bool IsDirectory(string physicalPath)
        => FindEntry(_paths.ToGitPath(physicalPath)) is { IsDirectory: true };

    /// <summary>
    /// A text that is the same for two states exactly when the permissions of the path are decided alike in both: it lists, for the root and
    /// each directory down to the path, whether it was there (and whether as a directory), and which configuration file it had.
    /// </summary>
    public string WhatPermissionsOfPathDependOn(string gitPath)
    {
        StringBuilder key = new();
        string[] names = gitPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int depth = 0; depth <= names.Length; depth++)
        {
            // Nothing below a path that wasn't there can make a difference
            if (FindEntry(string.Join('/', names[..depth])) is not { } entry)
                return key.Append('-').ToString();

            _ = key.Append(entry.IsDirectory ? 'd' : 'f');
            // A configuration file is told apart by its contents (the id of its blob)
            if (entry.Target is Tree directory)
                _ = key.Append(ConfigurationFileIn(directory)?.Target.Sha ?? "");
            _ = key.Append('/');
        }

        return key.ToString();
    }

    public void Dispose()
    {
        if (_ownsRepository)
            _repository.Dispose();
    }

    private static TreeEntry? ConfigurationFileIn(Tree directory)
        => directory.FirstOrDefault(child => child.TargetType == TreeEntryTargetType.Blob
                                             && string.Equals(child.Name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What is an entry of the notes: files and directories, but not the configuration files (which are about the permissions, not notes) or
    /// the app's own data, and nothing git keeps that the notes have no such thing as (links, submodules).
    /// </summary>
    private static bool IsEntryOfTheNotes(TreeEntry child, bool isRoot)
        => child.TargetType switch
        {
            TreeEntryTargetType.Tree => !(isRoot && child.Name == PathService.ReservedName),
            TreeEntryTargetType.Blob => child.Mode != Mode.SymbolicLink && !string.Equals(child.Name, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    /// <summary>The entry of that name: spelled exactly so if there is one, otherwise spelled in any case (as on a disk that ignores case).</summary>
    private static TreeEntry? FindChild(Tree directory, string name, bool isRoot)
        => directory.FirstOrDefault(child => child.Name == name && IsEntryOfTheNotes(child, isRoot))
           ?? directory.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase) && IsEntryOfTheNotes(child, isRoot));
}
