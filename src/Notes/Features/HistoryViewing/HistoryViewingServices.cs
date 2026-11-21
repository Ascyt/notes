using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Assets;
using Notes.Features.HistoryViewing.Changes;
using Notes.Features.HistoryViewing.Commits;
using Notes.Features.HistoryViewing.Requests;
using Notes.Features.HistoryViewing.States;
using Notes.Features.HistoryViewing.Views;

namespace Notes.Features.HistoryViewing;

public static class HistoryViewingServices
{
    /// <summary>
    /// Earlier states (<c>?before=</c>, <c>?commit=</c>), who may see them (docs/development/history-specification.md), and what a
    /// commit changed (<c>?commit=&amp;changes</c>).
    /// </summary>
    public static IServiceCollection AddHistoryViewing(this IServiceCollection services)
    {
        // Its texts are the site's to replace (see HistoryTexts)
        services.TryAddSingleton<HistoryTexts>();
        return services
            .AddComponentStyles(_HistoryModal.Styles, _EarlierStateCard.Styles, _CommitSidebar.Styles, _FileChanges.Styles)
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
}
