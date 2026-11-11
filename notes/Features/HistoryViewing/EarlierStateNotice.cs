using notes.Extensions;

namespace notes.Features.HistoryViewing;

/// <summary>
/// Why an address with <c>?before=</c> was sent to the entry as it is now instead (docs/development/history-specification.md, "Addresses that
/// can't be shown"). The redirect leaves it in a short-lived cookie, and the page it leads to shows it once, as an error message.
/// </summary>
public static class EarlierStateNotice
{
    public const string InvalidTime = "invalid-time";
    public const string FutureTime = "future-time";
    public const string NoHistory = "no-history";
    public const string NotPermitted = "not-permitted";
    public const string NotKeptInHistory = "not-kept";
    public const string DidNotExistThen = "did-not-exist";

    private const string CookieName = ".notes.history-notice";

    public static void Set(HttpContext context, string notice)
        => context.SetNoticeCookie(CookieName, notice, TimeSpan.FromMinutes(1));

    /// <summary>The message a redirect left for this page, if any; it is shown only once.</summary>
    public static string? TakeMessage(HttpContext context)
        => context.TakeNoticeCookie(CookieName) switch
        {
            InvalidTime => "The time of the earlier state is not a valid time.",
            FutureTime => "The time of the earlier state is in the future.",
            NoHistory => "There is no history of the notes.",
            NotPermitted => "Insufficient permissions to see this entry as it was at that time.",
            NotKeptInHistory => "The configuration leaves this entry out of the history.",
            DidNotExistThen => "This entry was not there at that time.",
            _ => null
        };
}
