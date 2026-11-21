namespace Notes.Features.History;

public static class HistoryServices
{
    /// <summary>The history: a git repository in _/history, see docs/administration/history.md.</summary>
    public static IServiceCollection AddHistory(this IServiceCollection services)
        => services
            .AddSingleton<HistoryService>()
            .AddScoped<CommitIdleChangesJob>();
}
