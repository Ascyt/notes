namespace Notes.Core.Disk.Config;

public union Option(bool, string);

public sealed class Model
{
    public bool? EnableTrash { get; set; } = null;
}
