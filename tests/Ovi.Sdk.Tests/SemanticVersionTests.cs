using Ovi.Sdk.Nodes;
using Xunit;

namespace Ovi.Sdk.Tests;

public class SemanticVersionTests
{
    [Fact]
    public void Parses_full_version()
    {
        var version = SemanticVersion.Parse("1.2.3-beta.1+build.5");

        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Equal("beta.1", version.Prerelease);
        Assert.Equal("build.5", version.BuildMetadata);
        Assert.True(version.IsPrerelease);
        Assert.Equal("1.2.3-beta.1+build.5", version.ToString());
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-beta..1")]
    [InlineData("1.0.0-beta_1")]
    [InlineData("-1.0.0")]
    [InlineData("abc")]
    public void Rejects_invalid_versions(string text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void Orders_by_semver_precedence()
    {
        string[] ascending =
        [
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
            "1.0.1",
            "1.1.0",
            "2.0.0",
        ];

        for (var i = 1; i < ascending.Length; i++)
        {
            var lower = SemanticVersion.Parse(ascending[i - 1]);
            var higher = SemanticVersion.Parse(ascending[i]);
            Assert.True(lower.CompareTo(higher) < 0, $"{lower} should precede {higher}");
            Assert.True(higher.CompareTo(lower) > 0, $"{higher} should follow {lower}");
        }
    }

    [Fact]
    public void Build_metadata_does_not_affect_precedence()
    {
        var plain = SemanticVersion.Parse("1.0.0");
        var withBuild = SemanticVersion.Parse("1.0.0+build.7");

        Assert.Equal(0, plain.CompareTo(withBuild));
        Assert.NotEqual(plain, withBuild); // equality still distinguishes them
    }
}
