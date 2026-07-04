namespace Notes.Core.Disk.Directory;

public sealed class Service(Options options) : IService
{
    private const string ConfigFileName = ".config.json";
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string _rootDir = System.IO.Path.GetFullPath(options.Directory);
    private readonly SemaphoreSlim _sync = new(1, 1);
    private Model? _rootModel;
    private bool _treeLoaded;

    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<Model?> LoadRootModelAsync(string rootDir)
    {
        await EnsureTreeLoadedAsync();

        await _sync.WaitAsync();
        try
        {
            if (_rootModel is null)
                return null;

            if (!TryGetRelativePathFromRoot(rootDir, out string relativePath))
                return null;

            Model? node = GetNodeByRelativePath(_rootModel, relativePath);
            return node is null ? null : CloneModel(node);
        }
        finally
        {
            _sync.Release();
        }
    }

    public Task<Model?> LoadModelAsync(string dir)
    {
        return LoadRootModelAsync(dir);
    }

    public async Task SaveModelAsync(string dir, Model model)
    {
        await WriteConfigAsync(dir, new DirectoryConfig
        {
            Name = model.Name,
            EnableTrash = model.EnableTrash
        });

        // Keep derived values and tree shape in sync after any persisted config change.
        await RefreshTreeAsync();
    }

    public async Task RefreshTreeAsync()
    {
        await _sync.WaitAsync();
        try
        {
            if (!System.IO.Directory.Exists(_rootDir))
            {
                _rootModel = null;
                _treeLoaded = true;
                return;
            }

            _rootModel = await BuildTreeModelAsync(_rootDir);
            _treeLoaded = true;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task EnsureTreeLoadedAsync()
    {
        if (_treeLoaded)
            return;

        await RefreshTreeAsync();
    }

    public async Task<bool?> ResolveEnableTrashAsync(string dir)
    {
        await EnsureTreeLoadedAsync();

        await _sync.WaitAsync();
        try
        {
            if (_rootModel is null)
                return null;

            if (!TryGetRelativePathFromRoot(dir, out string relativePath))
                return null;

            bool? resolved = _rootModel.EnableTrash;
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
                return resolved;

            Model current = _rootModel;
            string[] parts = relativePath.Split(
                [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (current.Subdirectories is null || !current.Subdirectories.TryGetValue(part, out Model? next) || next is null)
                    return null;

                current = next;
                resolved = current.EnableTrash ?? resolved;
            }

            return resolved;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<Model> BuildTreeModelAsync(string dir)
    {
        DirectoryConfig config = await EnsureValidConfigAsync(dir);

        Model currentModel = new()
        {
            Name = config.Name,
            Files = [],
            Subdirectories = [],
            EnableTrash = config.EnableTrash
        };

        foreach (string filePath in System.IO.Directory.GetFiles(dir))
        {
            string fileName = System.IO.Path.GetFileName(filePath);
            if (string.Equals(fileName, ConfigFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            System.IO.FileInfo info = new(filePath);
            currentModel.Files[fileName] = new File.Model
            {
                Name = fileName,
                Size = info.Length,
                CreatedAtUtc = info.CreationTimeUtc,
                ModifiedAtUtc = info.LastWriteTimeUtc
            };
        }

        foreach (string subdirectoryPath in System.IO.Directory.GetDirectories(dir))
        {
            string subdirectoryName = System.IO.Path.GetFileName(subdirectoryPath);
            currentModel.Subdirectories[subdirectoryName] = await BuildTreeModelAsync(subdirectoryPath);
        }

        return currentModel;
    }

    private async Task<DirectoryConfig> EnsureValidConfigAsync(string dir)
    {
        string configPath = System.IO.Path.Combine(dir, ConfigFileName);

        if (System.IO.File.Exists(configPath))
        {
            try
            {
                string existingJson = await System.IO.File.ReadAllTextAsync(configPath);
                DirectoryConfig? existingConfig = System.Text.Json.JsonSerializer.Deserialize<DirectoryConfig>(existingJson);
                if (existingConfig is not null && !string.IsNullOrWhiteSpace(existingConfig.Name))
                    return existingConfig;
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall through and recreate a valid config.
            }
        }

        DirectoryConfig newConfig = new()
        {
            Name = GetDefaultDirectoryName(dir),
            EnableTrash = null
        };

        await WriteConfigAsync(dir, newConfig);
        return newConfig;
    }

    private async Task WriteConfigAsync(string dir, DirectoryConfig config)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(config, _jsonOptions);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(dir, ConfigFileName), json);
    }

    private bool TryGetRelativePathFromRoot(string dir, out string relativePath)
    {
        string fullPath = System.IO.Path.GetFullPath(dir);
        if (fullPath.Equals(_rootDir, PathComparison))
        {
            relativePath = string.Empty;
            return true;
        }

        string rootWithSeparator = _rootDir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, PathComparison))
        {
            relativePath = string.Empty;
            return false;
        }

        relativePath = System.IO.Path.GetRelativePath(_rootDir, fullPath);
        return !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static Model? GetNodeByRelativePath(Model root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
            return root;

        Model current = root;
        string[] parts = relativePath.Split(
            [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string part in parts)
        {
            if (current.Subdirectories is null || !current.Subdirectories.TryGetValue(part, out Model? next) || next is null)
                return null;

            current = next;
        }

        return current;
    }

    private static Model CloneModel(Model model)
    {
        return new()
        {
            Name = model.Name,
            EnableTrash = model.EnableTrash,
            Files = model.Files?.ToDictionary(
                pair => pair.Key,
                pair => new File.Model
                {
                    Name = pair.Value.Name,
                    Size = pair.Value.Size,
                    CreatedAtUtc = pair.Value.CreatedAtUtc,
                    ModifiedAtUtc = pair.Value.ModifiedAtUtc
                }),
            Subdirectories = model.Subdirectories?.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is null ? null : CloneModel(pair.Value))
        };
    }

    private static string GetDefaultDirectoryName(string dir)
    {
        string trimmed = dir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        string name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private sealed class DirectoryConfig
    {
        public required string Name { get; set; }
        public bool? EnableTrash { get; set; }
    }
}
