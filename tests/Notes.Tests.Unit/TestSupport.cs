using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Addresses;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.Configuration.Migrations;
using Notes.Features.Helpers;

namespace Notes.Tests.Unit;

/// <summary>A notes directory of its own for a test, deleted afterwards, with the services that decide permissions on it.</summary>
public sealed class TempNotes : IDisposable
{
    public TempNotes()
    {
        Root = Path.Combine(Path.GetTempPath(), "notes-unit", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);
        Config = new Config { Dir = Root, Auth = new AuthConfig { Authority = "https://auth.invalid", ClientId = "c", ClientSecret = "s" } };
        Migrator = new ConfigMigrator(ConfigVersions.Current, ConfigMigrations.All, NullLogger<ConfigMigrator>.Instance);
        Locations = new EntryLocations(Config);
        Paths = new PathService(Locations);
        Store = new FolderConfigStore(Locations, Migrator, NullLogger<FolderConfigStore>.Instance);
        Access = new EntryAccess(Locations, Store);
        Exposed = new ExposedFolders(Config, new AppAddresses(Config));
        Tree = new AccessTree(Locations, Paths, Access, Exposed, new Notes.Features.Auth.AuthTexts());
        Files = new NotesTree(Root);
    }

    public string Root { get; }
    public Config Config { get; }
    public EntryLocations Locations { get; }
    public PathService Paths { get; }
    public ConfigMigrator Migrator { get; }
    public FolderConfigStore Store { get; }
    public EntryAccess Access { get; }
    public ExposedFolders Exposed { get; }
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
