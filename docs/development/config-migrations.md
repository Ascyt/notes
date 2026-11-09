# Migrations of the permissions files

How the format of `.config.json` is changed without breaking the files of existing installations. What versions mean for the files, and when they are migrated, is in [Permissions](../administration/permissions.md#versions-and-migrations).

## Adding a migration

1. The format is changed in the code (`PermissionParser`, `ConfigSchema`, `ConfigText.Default`, …) and in [Permissions](../administration/permissions.md).
2. A class implements `IConfigMigration` (`notes/Features/Permissions/Migrations`): `Version` (what a file is at afterwards), `Description`, and `Apply(JsonObject root)`, which changes the file's root object in place. It sees the format of the version before it, and does not set `version`. It throws an `InvalidOperationException` if the file isn't what it can work with: nothing is saved then.
3. It is added to `ConfigMigrations.All`, and `Config:Version` in `appsettings.json` is raised to its version (or higher; the app doesn't start if a migration leads past it, or if two lead to the same version). The version follows what changed: major for what old files can't be read as, minor for something new, patch for a fix.
4. A test reads a file of the old format (`ConfigMigratorTests` shows how), and `Cfg.Version` in the tests becomes the new version.

For example, a migration that moves `perms` into an object `access`:

```csharp
public sealed class MovePermsIntoAccess : IConfigMigration
{
    public SemVersion Version => new(1, 1, 0);
    public string Description => "perms moves into access.perms";

    public void Apply(JsonObject root)
    {
        JsonNode perms = root["perms"] ?? throw new InvalidOperationException("There is no perms.");
        _ = root.Remove("perms");
        root["access"] = new JsonObject { ["perms"] = perms };
    }
}
```

Migrations are never changed or removed once they were released: a file can be as old as the first version.
