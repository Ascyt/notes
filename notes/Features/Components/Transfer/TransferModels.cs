namespace notes.Features.Components.Transfer;

public enum TransferKind
{
    Copy,
    Move,
    /// <summary>Not a transfer as such, but the same transaction without a target: everything goes into the staging folder, which is then deleted.</summary>
    Delete
}

/// <summary>The outcome of a transfer. A failure leaves everything exactly as it was.</summary>
/// <param name="StatusCode">HTTP status to answer with.</param>
/// <param name="Message">What went wrong, for the user; empty on success.</param>
/// <param name="LeftOutUnreadable">After a successful copy: whether entries the user may not read were left out.</param>
public sealed record TransferResult(int StatusCode, string Message, bool LeftOutUnreadable = false)
{
    public bool IsSuccess => StatusCode == StatusCodes.Status200OK;

    public static TransferResult Success { get; } = new(StatusCodes.Status200OK, "");
}

/// <summary>A top-level entry to transfer.</summary>
/// <param name="Source">Physical path of the entry.</param>
/// <param name="Name">Name the entry has in the target directory (same as its own).</param>
public sealed record TransferItem(string Source, string Name, bool IsDirectory)
{
    /// <summary>
    /// For a copy of a directory: exactly what inside it is copied, each directory before what is in it. Entries the user may not read are
    /// not part of it (nor what is in them), and neither is anything that appeared after the permissions were checked.
    /// </summary>
    public IReadOnlyList<InsideEntry> Contents { get; init; } = [];
}

/// <summary>An entry inside a directory that is copied.</summary>
/// <param name="Path">Its physical path.</param>
public sealed record InsideEntry(string Path, bool IsDirectory);

/// <summary>What the <see cref="TransferPlanner"/> found is to be done.</summary>
/// <param name="LeftOutUnreadable">Whether a copy leaves out entries that the user may not read.</param>
public sealed record TransferPlan(List<TransferItem> Items, bool LeftOutUnreadable);

/// <summary>Ends a transfer early, with this result.</summary>
/// <param name="keepsStaging">Whether the staging folder must survive because it holds something that exists nowhere else.</param>
public sealed class TransferException(int statusCode, string message, bool keepsStaging = false) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public bool KeepsStaging { get; } = keepsStaging;
}

/// <summary>Comparing physical paths the way the notes do: case-insensitively, ignoring a trailing separator.</summary>
public static class TransferPaths
{
    public static bool IsSame(string a, string b)
        => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="child"/> is somewhere below <paramref name="parent"/>.</summary>
    public static bool IsInside(string child, string parent)
        => Path.TrimEndingDirectorySeparator(child).StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
