using notes.Features.HistoryViewing.Requests;

namespace notes.Tests.Unit;

/// <summary>How the time of an earlier state is written in the address (<c>?before=</c>).</summary>
public sealed class EarlierStateTimeTests
{
    [Theory]
    [InlineData("2026-11-11T13:30:00Z", "2026-11-11T13:30:00Z")]
    [InlineData("2026-11-11T13:30Z", "2026-11-11T13:30:00Z")]
    [InlineData("2026-11-11T14:30:00+01:00", "2026-11-11T13:30:00Z")]
    [InlineData("2026-11-11T13:30:00.1234567Z", "2026-11-11T13:30:00Z")]
    [InlineData("2026-11-11T08:30:00-05:00", "2026-11-11T13:30:00Z")]
    public void Times_with_a_time_zone_are_read_and_written_in_utc_to_the_second(string text, string expected)
    {
        Assert.True(EarlierStateTime.TryParse(text, out DateTimeOffset time));
        Assert.Equal(expected, EarlierStateTime.Format(time));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-11-11T13:30:00")]
    [InlineData("2026-11-11")]
    [InlineData("13:30:00Z")]
    [InlineData("2026-13-45T13:30:00Z")]
    [InlineData("2026-11-11 13:30:00Z")]
    [InlineData("yesterday")]
    [InlineData(" 2026-11-11T13:30:00Z")]
    public void Anything_else_is_not_a_time(string? text)
        => Assert.False(EarlierStateTime.TryParse(text, out _));
}
