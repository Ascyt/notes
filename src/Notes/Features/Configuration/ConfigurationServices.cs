using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Assets;
using Notes.Features.Configuration.Migrations;
using Notes.Features.EntryActions;
using Notes.Pages.ConfigEditor;

namespace Notes.Features.Configuration;

public static class ConfigurationServices
{
    /// <summary>
    /// The <c>.config.json</c> of each folder and its editor. Files from older versions of the format (<see cref="ConfigVersions.Current"/>) are
    /// migrated when read. Migrations that don't fit the version don't let the app start.
    /// </summary>
    public static IServiceCollection AddConfiguration(this IServiceCollection services)
    {
        // What is wrong with a configuration is told in its texts, the site's to replace (see ConfigTexts)
        services.TryAddSingleton<ConfigTexts>();
        _ = services.AddSingleton<ConfigSchema>();
        _ = services.AddSingleton(provider => new ConfigMigrator(ConfigVersions.Current, ConfigMigrations.All,
            provider.GetRequiredService<ILogger<ConfigMigrator>>(), provider.GetRequiredService<ConfigTexts>()));
        _ = services.AddSingleton<FolderConfigStore>();
        _ = services.AddScoped<RescanConfigsJob>();
        // The configuration page: its texts are the site's to replace (see ConfigEditorTexts)
        services.TryAddSingleton<ConfigEditorTexts>();
        _ = services.AddComponentStyles(ConfigEditorView.Styles);
        return services.AddEntryAction<ConfigHandler>();
    }
}
