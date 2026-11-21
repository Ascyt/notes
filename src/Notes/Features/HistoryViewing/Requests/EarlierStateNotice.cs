using Notes.Extensions;

namespace Notes.Features.HistoryViewing.Requests;

/// <summary>
/// Why an address with <c>?before=</c> or <c>?commit=</c> was sent to the entry as it is now instead (docs/development/history-specification.md,
/// "Addresses that
/// can't be shown"), or the changes of a commit to its state. The redirect leaves it in a short-lived cookie, and the page it leads to shows it
/// once, as an error message.
/// </summary>
public static class EarlierStateNotice
{
    public const string InvalidTime = "invalid-time";
    public const string FutureTime = "future-time";
    public const string NoHistory = "no-history";
    public const string NotPermitted = "not-permitted";
    public const string NotKeptInHistory = "not-kept";
    public const string DidNotExistThen = "did-not-exist";
    public const string InvalidCommit = "invalid-commit";
    public const string NoSuchCommit = "no-such-commit";
    public const string NotChangedByCommit = "not-changed";

    private const string CookieName = ".notes.history-notice";

    public static void Set(HttpContext context, string notice)
        => context.SetNoticeCookie(CookieName, notice, TimeSpan.FromMinutes(1));

    /// <summary>The notice a redirect left for this page (told by <see cref="HistoryTexts.Notice"/>), if any; it is shown only once.</summary>
    public static string? Take(HttpContext context)
        => context.TakeNoticeCookie(CookieName);
}
