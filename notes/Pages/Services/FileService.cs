using System.Text;

namespace notes.Pages.Services;

public sealed class FileService : IFileService
{
    public async Task SaveAsync(string physicalPath, Stream body)
    {
        using StreamReader reader = new(body, Encoding.UTF8);
        string contents = await reader.ReadToEndAsync();
        await File.WriteAllTextAsync(physicalPath, contents);
    }
}

public interface IFileService
{
    Task SaveAsync(string physicalPath, Stream body);
}
