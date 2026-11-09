using notes.Features.KillSwitch;

namespace notes.Tests.Unit;

public sealed class KillSwitchTests
{
    [Theory]
    [InlineData("http://keycloak:8080/realms/notes/.well-known/openid-configuration", "http://keycloak:8080/admin/realms/notes")]
    [InlineData("https://auth.example.com/realms/notes", "https://auth.example.com/admin/realms/notes")]
    [InlineData("http://localhost:8080/auth/realms/notes/", "http://localhost:8080/auth/admin/realms/notes")]
    public void The_admin_address_of_the_realm_comes_from_an_address_within_it(string realmAddress, string expected)
        => Assert.Equal(expected, KillSwitchService.AdminRealmUrl(realmAddress));

    [Fact]
    public void An_address_without_a_realm_is_refused()
        => Assert.Throws<InvalidOperationException>(() => KillSwitchService.AdminRealmUrl("http://keycloak:8080/"));
}
