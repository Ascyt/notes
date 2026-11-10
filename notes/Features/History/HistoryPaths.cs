namespace notes.Features.History;

/// <summary>
/// The paths of the history: relative to the root of the notes, with forward slashes (<c>docs/a.md</c>), and <c>""</c> for the root itself. They
/// are compared without case, as the notes are.
/// </summary>
public sealed class HistoryPaths(string root)
{
    /// <summary>The root of the notes (physical), which is the work tree of the repository.</summary>
    public string Root { get; } = Path.GetFullPath(root);

    public string ToGitPath(string physicalPath)
    {
        string relative = Path.GetRelativePath(Root, Path.GetFullPath(physicalPath)).Replace('\\', '/');
        return relative == "." ? "" : relative;
    }

    public string ToPhysicalPath(string gitPath)
        => Path.Combine(Root, gitPath.Replace('/', Path.DirectorySeparatorChar));

    public bool IsRoot(string physicalFolder)
        => string.Equals(Path.TrimEndingDirectorySeparator(physicalFolder), Path.TrimEndingDirectorySeparator(Root), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the path is the other one, or somewhere below it.</summary>
    public static bool IsAtOrBelow(string path, string other)
        => string.Equals(path, other, StringComparison.OrdinalIgnoreCase) || path.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder the path is in, <c>""</c> for what is in the root; null for the root itself.</summary>
    public static string? FolderOf(string path)
        => path == "" ? null : path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : "";
}
