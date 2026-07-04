namespace Notes.Core.Disk.Directory;

public interface IService
{
    public Task SaveModelAsync(string dir, Model model);
    public Task<Model> LoadModelAsync(string dir);
}
