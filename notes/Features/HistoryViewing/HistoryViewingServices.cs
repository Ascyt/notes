using notes.Features.HistoryViewing.Changes;
using notes.Features.HistoryViewing.Commits;
using notes.Features.HistoryViewing.Requests;
using notes.Features.HistoryViewing.States;
using notes.Features.HistoryViewing.Views;

namespace notes.Features.HistoryViewing;

public static class HistoryViewingServices
{
    /// <summary>
    /// Earlier states of the notes (<c>?before=</c>, <c>?commit=</c>), who may see them (docs/development/history-specification.md), and what a
    /// commit changed (<c>?commit=&amp;changes</c>).
    /// </summary>
    public static IServiceCollection AddHistoryViewing(this IServiceCollection services)
        => services
            .AddSingleton<EarlierStateReader>()
            .AddSingleton<EarlierStatePermissions>()
            .AddSingleton<EarlierStateCommits>()
            .AddSingleton<EarlierStateRequests>()
            .AddSingleton<EarlierStateViews>()
            .AddSingleton<CommitChanges>()
            .AddSingleton<CommitChangesRequests>()
            .AddSingleton<CommitChangesViews>()
            .AddSingleton<HistoryRequests>()
            .AddSingleton<HistoryButtons>();
}
