namespace Notes.Core.Disk.Config;

public sealed class Service : IService
{
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task SaveConfigAsync(string dir, Model config)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(config, _jsonOptions);

        await File.WriteAllTextAsync(Path.Combine(dir, ".config.json"), json);
    }

    public async Task<Model> LoadConfigAsync(string dir)
    {
        string configPath = Path.Combine(dir, ".config.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found in directory: {dir}");

        string json = await File.ReadAllTextAsync(configPath);
        return System.Text.Json.JsonSerializer.Deserialize<Model>(json) ?? throw new InvalidOperationException("Failed to deserialize config.");
    }
}
