namespace Notes.Core.Disk.Directory;

/// <summary>
/// Represents a directory with its data and config.
/// </summary>
public sealed class Model
{
    public required string Path { get; set; }
    public required Config.Model Config { get; set; }
}