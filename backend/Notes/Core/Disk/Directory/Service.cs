namespace Notes.Core.Disk.Directory;

public sealed class Service : IService
{
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task SaveModelAsync(string dir, Model model)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(model, _jsonOptions);

        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(dir, ".config.json"), json);
    }

    public async Task<Model> LoadModelAsync(string dir)
    {
        string configPath = System.IO.Path.Combine(dir, ".config.json");
        if (!System.IO.File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found in directory: {dir}");

        string json = await System.IO.File.ReadAllTextAsync(configPath);
        return System.Text.Json.JsonSerializer.Deserialize<Model>(json) ?? throw new InvalidOperationException("Failed to deserialize config.");
    }
}
