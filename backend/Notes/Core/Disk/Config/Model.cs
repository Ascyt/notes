namespace Notes.Core.Disk.Config;

public sealed class Model
{
    public required string Name { get; set; }
    public required Dictionary<string, FileConfig> Files { get; set; }
    public bool? EnableTrash { get; set; } = null;

    public Model WithUpper(Model other)
    {
        if (other == null) 
            return this;

        EnableTrash = other.EnableTrash ?? EnableTrash;
        return this;
    }
}

public sealed class FileConfig
{
    public required string Name { get; set; }
}
