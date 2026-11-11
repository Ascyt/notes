using System.Text.RegularExpressions;

namespace notes.Features.HistoryViewing.Requests;

/// <summary>
/// What in the address says which earlier state a page shows, and is kept on every link of it (docs/development/history-specification.md):
/// <c>before=</c> a time (the last state before it) or <c>commit=</c> the id of a commit (the state it made).
/// </summary>
/// <param name="Name"><see cref="Before"/> or <see cref="Commit"/>.</param>
/// <param name="Value">The time (as <see cref="EarlierStateTime.Format"/> writes it) or the id of the commit.</param>
public sealed partial record EarlierStateParameter(string Name, string Value)
{
    /// <summary>The query parameter that asks for the last state before a time (UTC, ISO 8601).</summary>
    public const string Before = "before";

    /// <summary>The query parameter that asks for the state a commit made, by its id (the full hash).</summary>
    public const string Commit = "commit";

    // The full hash of a commit, as git writes it
    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitIdPattern();

    public static EarlierStateParameter ForTime(DateTimeOffset time) => new(Before, EarlierStateTime.Format(time));

    public static EarlierStateParameter ForCommit(string id) => new(Commit, id);

    /// <summary>Whether the query asks for an earlier state at all.</summary>
    public static bool IsIn(IQueryCollection query) => query.ContainsKey(Before) || query.ContainsKey(Commit);

    /// <summary>Whether the text can be the id of a commit. Upper case is accepted, as git does; ids are compared in lower case.</summary>
    public static bool IsCommitId(string? text) => text is not null && CommitIdPattern().IsMatch(text.ToLowerInvariant());

    public bool IsCommit => Name == Commit;

    /// <summary>As it goes into a query: <c>before=2026-11-11T13:30:00Z</c>. Neither a time nor an id needs escaping there.</summary>
    public string QueryPart => Name + "=" + Value;
}
