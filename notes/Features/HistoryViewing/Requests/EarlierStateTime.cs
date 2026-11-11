using System.Globalization;
using System.Text.RegularExpressions;

namespace notes.Features.HistoryViewing.Requests;

/// <summary>
/// How the time of an earlier state is written in the address (<c>?before=</c>): ISO 8601 with a time zone, such as <c>2026-11-11T13:30:00Z</c>.
/// A time without one is refused, as it would mean something else in every time zone.
/// </summary>
public static partial class EarlierStateTime
{
    // Date, hours and minutes, optionally seconds and fractions of them, and the time zone: Z or an offset such as +01:00
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex IsoTimeWithTimeZone();

    public static bool TryParse(string? text, out DateTimeOffset time)
    {
        time = default;
        // The pattern makes sure of the time zone, which DateTimeOffset.TryParse would otherwise take as local time when it is missing
        return text is not null && IsoTimeWithTimeZone().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time);
    }

    /// <summary>As the address has it: UTC, to the second.</summary>
    public static string Format(DateTimeOffset time)
        => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
