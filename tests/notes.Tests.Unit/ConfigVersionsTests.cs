using notes.Features.Permissions.Migrations;
using Semver;

namespace notes.Tests.Unit;

/// <summary>What counts as a version of the configuration format (the comparing is the Semver library's).</summary>
public sealed class ConfigVersionsTests
{
    [Theory]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void Reads_major_minor_patch(string text, int major, int minor, int patch)
    {
        Assert.True(ConfigVersions.TryParse(text, out SemVersion? version));
        Assert.Equal(new SemVersion(major, minor, patch), version);
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.02.3")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3+build")]
    [InlineData("v1.2.3")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3\n")]
    [InlineData("1.a.3")]
    public void Refuses_everything_else(string? text)
    {
        Assert.False(ConfigVersions.TryParse(text, out _));
        Assert.Throws<FormatException>(() => ConfigVersions.Parse(text));
    }

    [Fact]
    public void Versions_compare_by_major_then_minor_then_patch()
    {
        string[] ascending = ["0.0.0", "0.0.1", "0.1.0", "0.10.0", "1.0.0", "1.0.9", "1.2.0", "2.0.0", "10.0.0"];

        for (int i = 0; i < ascending.Length; i++)
        {
            for (int j = 0; j < ascending.Length; j++)
            {
                SemVersion left = ConfigVersions.Parse(ascending[i]);
                SemVersion right = ConfigVersions.Parse(ascending[j]);
                Assert.Equal(i.CompareTo(j), Math.Sign(left.ComparePrecedenceTo(right)));
                Assert.Equal(i == j, left == right);
            }
        }
    }
}
