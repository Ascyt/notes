using notes.Features.Auth;

namespace notes.Tests.Unit;

/// <summary>Telling a session with Keycloak's "Remember me" from one without, by the lifetime of its refresh token (keycloak/notes-realm.json).</summary>
public sealed class RememberMeTests
{
    [Theory]
    [InlineData("604800", true)] // remembered: 7 days idle
    [InlineData("1800", false)] // not remembered: 30 minutes idle
    [InlineData("28800", false)] // not remembered: never past the 8 hours maximum
    [InlineData(null, false)]
    [InlineData("soon", false)]
    public void A_session_is_remembered_only_when_Keycloak_gave_it_a_long_lifetime(string? refreshExpiresIn, bool remembered)
        => Assert.Equal(remembered, AuthExtensions.IsRemembered(refreshExpiresIn));
}
