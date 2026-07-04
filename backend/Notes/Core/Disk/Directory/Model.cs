namespace Notes.Core.Disk.Directory;

/// <summary>
/// Represents a directory with its config.
/// </summary>
public sealed class Model
{
    public required string Name { get; set; }
    public required Dictionary<string, File.Model> Files { get; set; }
    public bool? EnableTrash { get; set; } = null;

    public Model WithUpper(Model other)
    {
        if (other == null) 
            return this;

        EnableTrash = other.EnableTrash ?? EnableTrash;
        return this;
    }
}
