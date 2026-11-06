using System.Security.Claims;
using notes.Features.Auth;
using notes.Features.Permissions;

namespace notes.Tests.Unit;

public sealed class PrincipalTests
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    private static IReadOnlySet<string> Set(params string[] principals) => new HashSet<string>(principals, StringComparer.Ordinal);

    [Theory]
    [InlineData("u:Alice", "u:alice")]
    [InlineData("u:ALICE", "u:alice")]
    [InlineData("g:Team", "g:Team")]
    [InlineData("r:Admin", "r:Admin")]
    [InlineData("a", "a")]
    [InlineData("l", "l")]
    public void Normalize(string text, string expected)
        => Assert.Equal(expected, Principal.Normalize(text, out _));

    [Fact]
    public void Too_long_principals_are_refused()
    {
        Assert.Null(Principal.Normalize("u:" + new string('x', Principal.MaxLength), out string? error));
        Assert.Contains("too long", error);
    }

    [Fact]
    public void A_user_is_their_name_their_id_their_groups_and_their_roles()
    {
        PrincipalSet set = PrincipalSet.Of(Principals.User("Alice", sub: "1234", groups: ["Team"], roles: ["editor"]));

        Assert.True(set.IsAnyOf(Set("u:alice")));
        Assert.True(set.IsAnyOf(Set("u:1234")));
        Assert.True(set.IsAnyOf(Set("g:Team")));
        Assert.True(set.IsAnyOf(Set("r:editor")));
    }

    [Fact]
    public void Group_and_role_names_are_case_sensitive()
    {
        PrincipalSet set = PrincipalSet.Of(Principals.User("alice", groups: ["team"], roles: ["editor"]));

        Assert.False(set.IsAnyOf(Set("g:Team")));
        Assert.False(set.IsAnyOf(Set("r:Editor")));
    }

    [Fact]
    public void A_logged_in_user_is_one_of_all_users_and_one_of_all_who_are_logged_in()
    {
        PrincipalSet set = PrincipalSet.Of(Principals.User("alice"));

        Assert.True(set.IsAnyOf(Set("a")));
        Assert.True(set.IsAnyOf(Set("l")));
    }

    [Fact]
    public void Someone_who_is_not_logged_in_is_only_one_of_all_users_whatever_claims_they_carry()
    {
        // Claims, but not authenticated (no authentication type)
        ClaimsPrincipal user = new(new ClaimsIdentity([
            new Claim(NotesClaims.UserName, "alice"), new Claim(NotesClaims.Groups, "team"), new Claim(NotesClaims.Roles, NotesAccess.AdminRole)
        ]));
        PrincipalSet set = PrincipalSet.Of(user);

        Assert.True(set.IsAnyOf(Set("a")));
        Assert.False(set.IsAnyOf(Set("l")));
        Assert.False(set.IsAnyOf(Set("u:alice")));
        Assert.False(set.IsAnyOf(Set("g:team")));
    }

    [Fact]
    public void A_user_name_that_looks_like_another_kind_of_principal_is_still_a_user_name()
    {
        PrincipalSet set = PrincipalSet.Of(Principals.User("g:team", sub: "r:admin"));

        Assert.False(set.IsAnyOf(Set("g:team")));
        Assert.False(set.IsAnyOf(Set("r:admin")));
        Assert.True(set.IsAnyOf(Set("u:g:team")));
    }

    [Fact]
    public void Other_claims_are_no_principals()
    {
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(NotesClaims.UserName, "alice"), new Claim("email", "x@y"), new Claim(ClaimTypes.Role, "admin")], "Test"));
        PrincipalSet set = PrincipalSet.Of(user);

        Assert.False(set.IsAnyOf(Set("r:admin")));
        Assert.False(set.IsAnyOf(Set("u:x@y")));
    }

    [Fact]
    public void An_empty_list_matches_nobody()
        => Assert.False(PrincipalSet.Of(Principals.User("alice")).IsAnyOf(Empty));

    // ---- One block

    private static PermsBlock Block(string[]? rwAllow = null, string[]? rwReadOnly = null, string[]? rwDeny = null,
        string[]? cdAllow = null, string[]? cdCreateOnly = null, string[]? cdDeny = null)
        => new(true, Set(rwAllow ?? []), Set(rwReadOnly ?? []), Set(rwDeny ?? []), Set(cdAllow ?? []), Set(cdCreateOnly ?? []), Set(cdDeny ?? []));

    private static readonly PrincipalSet Alice = PrincipalSet.Of(Principals.User("alice", groups: ["a", "b"]));

    [Fact]
    public void Within_a_block_deny_beats_allow_and_allow_beats_read_only()
    {
        Assert.Equal(ReadAccess.None, Block(rwAllow: ["g:a"], rwDeny: ["g:b"]).DecideRead(Alice));
        Assert.Equal(ReadAccess.ReadAndWrite, Block(rwAllow: ["g:a"], rwReadOnly: ["g:b"]).DecideRead(Alice));
        Assert.Equal(ReadAccess.Read, Block(rwReadOnly: ["u:alice"]).DecideRead(Alice));
        Assert.Null(Block(rwAllow: ["g:c"]).DecideRead(Alice));

        Assert.Equal(CreateAccess.None, Block(cdAllow: ["g:a"], cdDeny: ["g:b"]).DecideCreate(Alice));
        Assert.Equal(CreateAccess.CreateAndDelete, Block(cdAllow: ["g:a"], cdCreateOnly: ["g:b"]).DecideCreate(Alice));
        Assert.Equal(CreateAccess.Create, Block(cdCreateOnly: ["g:b"]).DecideCreate(Alice));
        Assert.Null(Block(cdDeny: ["g:c"]).DecideCreate(Alice));
    }

    [Fact]
    public void Levels_include_the_ones_below()
    {
        EntryAccessInfo all = new(ReadAccess.ReadAndWrite, CreateAccess.CreateAndDelete);
        Assert.True(all is { CanRead: true, CanWrite: true, CanCreate: true, CanDelete: true });
        Assert.True(new EntryAccessInfo(ReadAccess.Read, CreateAccess.Create) is { CanRead: true, CanWrite: false, CanCreate: true, CanDelete: false });
        Assert.True(EntryAccessInfo.None is { CanRead: false, CanWrite: false, CanCreate: false, CanDelete: false });
    }
}
