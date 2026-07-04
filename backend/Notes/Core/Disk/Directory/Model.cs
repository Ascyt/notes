namespace Notes.Core.Disk.Directory;

/// <summary>
/// Represents a directory with its config.
/// </summary>
public sealed class Model
{
    public required string Name { get; set; }
    public Dictionary<string, File.Model>? Files { get; set; } = null;
    public Dictionary<string, Model?>? Subdirectories { get; set; } = null;
    public bool? EnableTrash { get; set; } = null;

    public Model WithCombinedSettingsFrom(Model? other)
    {
        return new()
        {
            Name = Name,
            Files = Files,
            Subdirectories = Subdirectories,
            EnableTrash = EnableTrash ?? other?.EnableTrash,
        };
    }
}
