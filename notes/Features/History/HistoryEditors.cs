namespace notes.Features.History;

/// <summary>Who changed what since the last commit, by git path (see <see cref="HistoryPaths"/>). A directory stands for everything in it.</summary>
public sealed class HistoryEditors
{
    private readonly Dictionary<string, HistoryAuthor> _editors = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string path, HistoryAuthor author)
        => _editors[path] = author;

    /// <summary>Who changed the path: whoever changed it, or the directory it is in, last; the app if nobody in it did.</summary>
    public HistoryAuthor AuthorOf(string path)
    {
        for (string? current = path; current is not null; current = HistoryPaths.FolderOf(current))
        {
            if (_editors.TryGetValue(current, out HistoryAuthor? author))
                return author;
        }

        return HistoryAuthor.App;
    }

    /// <summary>Everything was committed.</summary>
    public void Clear()
        => _editors.Clear();
}
