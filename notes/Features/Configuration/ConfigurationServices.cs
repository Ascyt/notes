using notes.Features.Configuration.Migrations;
using notes.Features.EntryActions;

namespace notes.Features.Configuration;

public static class ConfigurationServices
{
    /// <summary>
    /// The <c>.config.json</c> of each folder and its editor. The version of the app is set in appsettings (Config:Version); files from older
    /// versions are migrated when read. Neither a version that isn't one nor migrations that don't fit it let the app start.
    /// </summary>
    public static IServiceCollection AddConfiguration(this IServiceCollection services)
    {
        _ = services.AddSingleton(provider => new ConfigMigrator(ConfigVersions.Parse(provider.GetRequiredService<Config>().Version),
            ConfigMigrations.All, provider.GetRequiredService<ILogger<ConfigMigrator>>()));
        _ = services.AddSingleton<FolderConfigStore>();
        return services.AddEntryAction<ConfigHandler>();
    }
}
