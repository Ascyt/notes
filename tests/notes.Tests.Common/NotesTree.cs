using System.Text.Json;
using System.Text.Json.Nodes;

namespace notes.Tests.Common;

/// <summary>Makes and looks at files and directories of a notes directory, by virtual path ("a/b.md").</summary>
public sealed class NotesTree(string root)
{
    public string Root { get; } = root;

    public string Physical(string virtualPath)
        => virtualPath == "" ? Root : Path.Combine(Root, virtualPath.Replace('/', Path.DirectorySeparatorChar));

    public string File(string virtualPath, string contents = "")
    {
        string physical = Physical(virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        System.IO.File.WriteAllText(physical, contents);
        return physical;
    }

    public string File(string virtualPath, byte[] contents)
    {
        string physical = Physical(virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        System.IO.File.WriteAllBytes(physical, contents);
        return physical;
    }

    public string Dir(string virtualPath)
        => Directory.CreateDirectory(Physical(virtualPath)).FullName;

    /// <summary>Writes the configuration file of the directory as it is (see <see cref="Cfg"/> to make one).</summary>
    public void Config(string directoryVirtualPath, string text)
    {
        string physical = Path.Combine(Dir(directoryVirtualPath), ".config.json");
        System.IO.File.WriteAllText(physical, text);
        // The app caches a configuration for as long as its time and length stay the same. The file system's clock may not have moved since
        // the last write (a test that changes the permissions right after a request), so every write gets a time of its own.
        System.IO.File.SetLastWriteTimeUtc(physical, DateTime.UtcNow.AddMilliseconds(Interlocked.Increment(ref _configWrites)));
    }

    private static long _configWrites;

    public string? ReadConfig(string directoryVirtualPath)
    {
        string physical = Path.Combine(Physical(directoryVirtualPath), ".config.json");
        return System.IO.File.Exists(physical) ? System.IO.File.ReadAllText(physical) : null;
    }

    public string Read(string virtualPath) => System.IO.File.ReadAllText(Physical(virtualPath));

    public bool Exists(string virtualPath) => Path.Exists(Physical(virtualPath));

    public bool IsFile(string virtualPath) => System.IO.File.Exists(Physical(virtualPath));

    public bool IsDirectory(string virtualPath) => Directory.Exists(Physical(virtualPath));

    /// <summary>Everything inside the directory (virtual paths, sorted), the reserved folder "_" left out unless asked for.</summary>
    public IReadOnlyList<string> List(string directoryVirtualPath = "", bool includeReserved = false)
        => Directory.EnumerateFileSystemEntries(Physical(directoryVirtualPath), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Where(path => includeReserved || !(path == "_" || path.StartsWith("_/", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>A snapshot of everything (names and contents), to check that an operation changed nothing.</summary>
    public string Snapshot()
        => string.Join('\n', List().Select(path => System.IO.File.Exists(Physical(path)) ? path + "=" + Read(path) : path + "/"));
}

/// <summary>Writes configuration files (docs/permissions.md) for the tests.</summary>
public static class Cfg
{
    /// <summary>A <c>perms</c> block.</summary>
    public static JsonObject Perms(bool inherit = true, string[]? rwAllow = null, string[]? rwReadOnly = null, string[]? rwDeny = null,
        string[]? cdAllow = null, string[]? cdCreateOnly = null, string[]? cdDeny = null)
    {
        JsonObject perms = new() { ["inherit"] = inherit };
        void Add(string key, string[]? list)
        {
            if (list is not null)
                perms[key] = new JsonArray([..list.Select(principal => (JsonNode)principal)]);
        }

        Add("rwAllow", rwAllow);
        Add("rwReadOnly", rwReadOnly);
        Add("rwDeny", rwDeny);
        Add("cdAllow", cdAllow);
        Add("cdCreateOnly", cdCreateOnly);
        Add("cdDeny", cdDeny);
        return perms;
    }

    /// <summary>The configuration of a folder, with its own permissions and those of some of its files.</summary>
    public static string Folder(JsonObject perms, params (string Name, JsonObject Perms)[] files)
        => new JsonObject
        {
            ["perms"] = perms,
            ["files"] = new JsonArray([..files.Select(file => (JsonNode)new JsonObject { ["name"] = file.Name, ["perms"] = file.Perms })])
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
