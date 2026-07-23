using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Operators;
using Xunit;

namespace Ovi.Sdk.Tests;

/// <summary>
/// Keeps <c>schemas/agent-definition.schema.json</c> and the <c>samples/agents</c> files aligned
/// with what the SDK actually parses.
/// </summary>
public class SchemaTests
{
    private static string SchemaPath => Path.Combine(AppContext.BaseDirectory, "schemas", "agent-definition.schema.json");

    private static string SamplePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "samples", "agents", fileName);

    [Fact]
    public void The_schema_is_valid_json_with_the_expected_shape()
    {
        var schema = JsonNode.Parse(File.ReadAllText(SchemaPath))!.AsObject();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]!.GetValue<string>());
        Assert.Equal("object", schema["type"]!.GetValue<string>());

        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["id", "name"], required);

        var properties = schema["properties"]!.AsObject().Select(property => property.Key).ToArray();
        Assert.Contains("id", properties);
        Assert.Contains("name", properties);
        Assert.Contains("description", properties);
        Assert.Contains("instructions", properties);
        Assert.Contains("tools", properties);
    }

    [Theory]
    [InlineData("acme/web-search@1.2.0", true)]
    [InlineData("acme/web-search@2.0.0-beta.1+build.5", true)]
    [InlineData("./manual-trigger", true)]
    [InlineData("./schedule-trigger@0.3.1", true)]
    [InlineData("acme/web-search", false)] // published ids require a version
    [InlineData("/tool@1.0.0", false)]
    [InlineData("acme/-tool@1.0.0", false)]
    [InlineData("acme/tool@01.0.0", false)]
    [InlineData("acme/a b@1.0.0", false)]
    public void The_schema_operator_id_pattern_agrees_with_OperatorId(string candidate, bool expected)
    {
        var schema = JsonNode.Parse(File.ReadAllText(SchemaPath))!;
        var pattern = schema["$defs"]!["operatorId"]!["pattern"]!.GetValue<string>();

        Assert.Equal(expected, Regex.IsMatch(candidate, pattern));
        Assert.Equal(expected, OperatorId.TryParse(candidate, out _));
    }

    [Fact]
    public void The_yaml_sample_loads_through_the_serializer()
    {
        var definition = AgentDefinitionSerializer.Load(SamplePath("researcher.yaml"));

        Assert.Equal(OperatorId.Parse("acme/researcher@1.0.0"), definition.Id);
        Assert.Equal("Researcher", definition.Name);
        Assert.Equal(2, definition.Tools.Count);
        Assert.Equal(5, definition.Tools[1].Options!["maxResults"]!.GetValue<int>());
    }

    [Fact]
    public void The_json_sample_loads_through_the_serializer()
    {
        // The $schema property in the sample is tooling metadata; the loader ignores it.
        var definition = AgentDefinitionSerializer.Load(SamplePath("support-triage.json"));

        Assert.Equal(OperatorId.Parse("acme/support-triage@0.1.0"), definition.Id);
        Assert.Equal("Support Triage", definition.Name);
        Assert.Equal(2, definition.Tools.Count);
        Assert.Equal("kb-search", definition.Tools[1].Id.Name);
        Assert.Equal(3, definition.Tools[1].Options!["topK"]!.GetValue<int>());
    }

    [Fact]
    public void Sample_ids_match_the_schema_pattern()
    {
        var schema = JsonNode.Parse(File.ReadAllText(SchemaPath))!;
        var pattern = schema["$defs"]!["operatorId"]!["pattern"]!.GetValue<string>();

        foreach (var file in new[] { "researcher.yaml", "support-triage.json" })
        {
            var definition = AgentDefinitionSerializer.Load(SamplePath(file));
            Assert.Matches(pattern, definition.Id.ToString());
            foreach (var tool in definition.Tools)
            {
                Assert.Matches(pattern, tool.Id.ToString());
            }
        }
    }
}
