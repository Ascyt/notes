namespace Notes.Core.Disk.Directory;

/// <summary>
/// Represents a directory with its data and config.
/// </summary>
public sealed class Model
{
    public string? Path { get; set; } = null;
    public required string Name { get; set; }
}