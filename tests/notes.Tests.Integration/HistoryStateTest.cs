using System.Net;
using Microsoft.Extensions.DependencyInjection;
using notes.Features.History;
using notes.Features.HistoryViewing;

namespace notes.Tests.Integration;

/// <summary>
/// What the tests of earlier states (<c>?before=</c>, docs/development/history-specification.md) share: each test makes its own history, one
/// commit at a time, with the fake clock.
/// </summary>
public abstract class HistoryStateTest : IntegrationTest
{
    protected HistoryStateTest(NotesAppFactory app) : base(app)
    {
        // The notes of the last test were removed with their history: what is there now is the first state
        Start = CommitNow();
    }

    /// <summary>A time of the first state of the test (the base tree, without anyone given the history).</summary>
    protected DateTimeOffset Start { get; }

    protected HistoryService History => App.Services.GetRequiredService<HistoryService>();

    /// <summary>Commits what is there, a minute after the last commit, and moves the clock past it. Returns a time that the new state is the state before.</summary>
    protected DateTimeOffset CommitNow()
    {
        App.Time.Advance(TimeSpan.FromMinutes(1));
        History.CommitPending(CommitReason.Idle);
        DateTimeOffset during = App.Time.GetUtcNow().AddSeconds(30);
        App.Time.Advance(TimeSpan.FromMinutes(1));
        return during;
    }

    protected static string At(DateTimeOffset time) => "before=" + EarlierStateTime.Format(time);

    /// <summary>The base tree's root permissions, with the history given (or denied) as the arguments say.</summary>
    protected void SetRoot(string[]? historyAllow = null, string[]? historyExistingOnly = null, string[]? historyDeny = null, string[]? rwDeny = null)
        => Tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["g:guests", "a", "u:carol"], rwDeny: rwDeny, cdAllow: ["g:team"],
            historyAllow: historyAllow, historyExistingOnly: historyExistingOnly, historyDeny: historyDeny)));

    protected static string? NoticeOf(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
            ? cookies.Select(cookie => cookie.Split(';')[0]).FirstOrDefault(cookie => cookie.StartsWith(".notes.history-notice="))?.Split('=')[1]
            : null;

    /// <summary>That the address was sent to the entry as it is now, with the reason.</summary>
    protected static void AssertLeft(HttpResponseMessage response, string expectedLocation, string expectedNotice)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
        Assert.Equal(expectedNotice, NoticeOf(response));
    }
}
