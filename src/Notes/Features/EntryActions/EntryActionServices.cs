using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Notes.Features.EntryActions;

public static class EntryActionServices
{
    /// <summary>The list of all actions (<see cref="EntryActions"/>), which the features fill with <see cref="AddEntryAction{TAction}"/>.</summary>
    public static IServiceCollection AddEntryActions(this IServiceCollection services)
    {
        // What the server answers about an entry in general, the site's to replace (see EntryTexts)
        services.TryAddSingleton<EntryTexts>();
        return services.AddSingleton<EntryActions>();
    }

    /// <summary>
    /// Registers the action (one instance for the app), so that the page of the entries answers its parameter. It can still be asked for
    /// by its own type, by what uses more of it than the action.
    /// </summary>
    public static IServiceCollection AddEntryAction<TAction>(this IServiceCollection services) where TAction : class, IEntryAction
    {
        _ = services.AddSingleton<TAction>();
        return services.AddSingleton<IEntryAction>(provider => provider.GetRequiredService<TAction>());
    }
}
