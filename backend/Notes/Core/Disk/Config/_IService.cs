namespace Notes.Core.Disk.Config;
public interface IService
{
    public Task SaveConfigAsync(string dir, Model config);
    public Task<Model> LoadConfigAsync(string dir);
}
