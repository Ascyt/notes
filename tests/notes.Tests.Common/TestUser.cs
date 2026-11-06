using System.Security.Claims;
using System.Text.Json;
using notes.Features.Auth;

namespace notes.Tests.Common;

/// <summary>
/// Someone using the app in a test. The tests don't go through Keycloak: the user is sent along with every request in the
/// <see cref="HeaderName"/> header, and <see cref="TestAuthentication"/> makes the same principal of it that a login would.
/// </summary>
public sealed record TestUser(string Name, string? Sub, string[] Groups, string[] Roles)
{
    public const string HeaderName = "X-Test-User";

    public bool IsAnonymous => Sub is null;

    /// <summary>The same user under another id (the selections and uploads are kept by id), so tests don't share them.</summary>
    public TestUser WithSub(string sub) => this with { Sub = sub };

    public string? ToHeader()
        => IsAnonymous ? null : JsonSerializer.Serialize(new Payload(Name, Sub!, Groups, Roles));

    /// <summary>The principal a login through Keycloak gives this user (see AuthExtensions: claim names as Keycloak sends them).</summary>
    public ClaimsPrincipal ToPrincipal()
    {
        if (IsAnonymous)
            return new ClaimsPrincipal(new ClaimsIdentity());

        List<Claim> claims = [new(NotesClaims.UserName, Name), new(NotesClaims.UserId, Sub!)];
        claims.AddRange(Groups.Select(group => new Claim(NotesClaims.Groups, group)));
        claims.AddRange(Roles.Select(role => new Claim(NotesClaims.Roles, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", NotesClaims.UserName, NotesClaims.Roles));
    }

    public static TestUser? FromHeader(string header)
    {
        Payload? payload = JsonSerializer.Deserialize<Payload>(header);
        return payload is null ? null : new TestUser(payload.Name, payload.Sub, payload.Groups, payload.Roles);
    }

    public override string ToString() => IsAnonymous ? "anonymous" : Name;

    private sealed record Payload(string Name, string Sub, string[] Groups, string[] Roles);
}

/// <summary>The users the tests use. What each may do follows from the configuration of <see cref="BaseTree"/>.</summary>
public static class Users
{
    /// <summary>Has the admin role: may do anything, sees the configuration files.</summary>
    public static TestUser Admin { get; } = new("admin", "admin-id", [], [NotesAccess.AdminRole]);

    /// <summary>In the group "team": reads, writes, creates and deletes in most of the notes, but not in the vault.</summary>
    public static TestUser Alice { get; } = new("alice", "alice-id", ["team"], []);

    /// <summary>In the group "guests": may only read what is public.</summary>
    public static TestUser Bob { get; } = new("bob", "bob-id", ["guests"], []);

    /// <summary>In the group "team", and the only one (but admins) with access to the vault.</summary>
    public static TestUser Carol { get; } = new("carol", "carol-id", ["team"], []);

    /// <summary>Logged in, but in no group: may not even read the root.</summary>
    public static TestUser Nobody { get; } = new("nobody", "nobody-id", [], []);

    /// <summary>Not logged in.</summary>
    public static TestUser Anonymous { get; } = new("", null, [], []);
}
