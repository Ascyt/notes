namespace Notes.Core.Disk.Directory;

public interface IService
{
    public Task SaveModelAsync(string dir, Model model);
    public Task<Model?> LoadModelAsync(string dir);
    public Task<Model?> LoadRootModelAsync(string dir);
    public Task RefreshTreeAsync();
    public Task<bool?> ResolveEnableTrashAsync(string dir);
}
