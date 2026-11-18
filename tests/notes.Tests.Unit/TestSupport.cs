using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using notes.Features.Auth;
using notes.Features.Bridges;
using notes.Features.Configuration;
using notes.Features.Configuration.Migrations;
using notes.Features.Helpers;

namespace notes.Tests.Unit;

/// <summary>A notes directory of its own for a test, deleted afterwards, with the services that decide permissions on it.</summary>
public sealed class TempNotes : IDisposable
{
    public TempNotes()
    {
        Root = Path.Combine(Path.GetTempPath(), "notes-unit", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);
        Config = new Config { Dir = Root, Version = Cfg.Version, Auth = new AuthConfig { Authority = "https://auth.invalid", ClientId = "c", ClientSecret = "s" } };
        Migrator = new ConfigMigrator(ConfigVersions.Parse(Config.Version), ConfigMigrations.All, NullLogger<ConfigMigrator>.Instance);
        Locations = new NotesLocations(Config);
        Paths = new PathService(Locations);
        Store = new FolderConfigStore(Locations, Migrator, NullLogger<FolderConfigStore>.Instance);
        Access = new EntryAccess(Locations, Store);
        Tree = new AccessTree(Locations, Paths, Access);
        Files = new NotesTree(Root);
    }

    public string Root { get; }
    public Config Config { get; }
    public NotesLocations Locations { get; }
    public PathService Paths { get; }
    public ConfigMigrator Migrator { get; }
    public FolderConfigStore Store { get; }
    public EntryAccess Access { get; }
    public AccessTree Tree { get; }
    public NotesTree Files { get; }

    public string P(string virtualPath) => Files.Physical(virtualPath);

    public EntryAccessInfo Get(TestUser user, string virtualPath) => Access.GetAccess(user.ToPrincipal(), P(virtualPath));

    public void Dispose()
    {
        try
        {
            TestDirectories.Delete(Root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public static class Principals
{
    public static ClaimsPrincipal User(string name, string? sub = null, string[]? groups = null, string[]? roles = null)
        => new TestUser(name, sub ?? name + "-id", groups ?? [], roles ?? []).ToPrincipal();
}
