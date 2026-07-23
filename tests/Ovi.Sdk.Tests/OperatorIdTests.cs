using System.Text.Json;
using Ovi.Sdk;
using Ovi.Sdk.Operators;
using Xunit;

namespace Ovi.Sdk.Tests;

public class OperatorIdTests
{
    [Fact]
    public void Parses_published_id_with_version()
    {
        var id = OperatorId.Parse("acme/web-search@1.2.0");

        Assert.Equal("acme", id.Organization);
        Assert.Equal("web-search", id.Name);
        Assert.Equal(new SemanticVersion(1, 2, 0), id.Version);
        Assert.False(id.IsBuiltIn);
        Assert.Equal("acme/web-search@1.2.0", id.ToString());
    }

    [Fact]
    public void Parses_built_in_id_with_implicit_version()
    {
        var id = OperatorId.Parse("./manual-trigger");

        Assert.True(id.IsBuiltIn);
        Assert.Equal(".", id.Organization);
        Assert.Equal("manual-trigger", id.Name);
        Assert.Null(id.Version);
        Assert.False(id.HasExplicitVersion);
        Assert.Equal("./manual-trigger", id.ToString());
    }

    [Fact]
    public void Parses_built_in_id_with_explicit_version()
    {
        var id = OperatorId.Parse("./schedule-trigger@0.3.1");

        Assert.True(id.IsBuiltIn);
        Assert.Equal(new SemanticVersion(0, 3, 1), id.Version);
    }

    [Fact]
    public void Rejects_published_id_without_version()
    {
        Assert.False(OperatorId.TryParse("acme/web-search", out _));
        Assert.Throws<FormatException>(() => OperatorId.Parse("acme/web-search"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme")]
    [InlineData("/tool@1.0.0")]
    [InlineData("acme//tool@1.0.0")]
    [InlineData("acme/a b@1.0.0")]
    [InlineData("acme/tool@not-a-version")]
    [InlineData("acme/-tool@1.0.0")]
    [InlineData("..-/tool@1.0.0")]
    public void Rejects_invalid_ids(string text) => Assert.False(OperatorId.TryParse(text, out _));

    [Fact]
    public void Equality_is_case_insensitive_on_organization_and_name()
    {
        var lower = OperatorId.Parse("acme/tool@1.0.0");
        var mixed = OperatorId.Parse("Acme/Tool@1.0.0");

        Assert.Equal(lower, mixed);
        Assert.Equal(lower.GetHashCode(), mixed.GetHashCode());
        Assert.True(lower == mixed);
    }

    [Fact]
    public void IsSameOperator_ignores_version()
    {
        var v1 = OperatorId.Parse("acme/tool@1.0.0");
        var v2 = OperatorId.Parse("acme/tool@2.0.0");

        Assert.True(v1.IsSameOperator(v2));
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void Converts_implicitly_from_string()
    {
        OperatorId id = "acme/tool@1.0.0";
        Assert.Equal("acme", id.Organization);
    }

    [Fact]
    public void Round_trips_through_json_as_string()
    {
        var descriptor = new OperatorDescriptor("acme/tool@1.0.0", "Tool", "A tool");

        var json = JsonSerializer.Serialize(descriptor, OviJson.DefaultOptions);
        Assert.Contains("\"acme/tool@1.0.0\"", json);

        var roundTripped = JsonSerializer.Deserialize<OperatorDescriptor>(json, OviJson.DefaultOptions)!;
        Assert.Equal(descriptor.Id, roundTripped.Id);
        Assert.Equal(descriptor.Name, roundTripped.Name);
        Assert.Equal(descriptor.Description, roundTripped.Description);
    }
}
