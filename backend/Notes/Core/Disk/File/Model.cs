namespace Notes.Core.Disk.File;

public sealed class Model
{
    public required string Name { get; set; }
    public required long Size { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public required DateTime ModifiedAtUtc { get; set; }
}
