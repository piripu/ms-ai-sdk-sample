using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Sdk.Tests;

/// <summary>
/// Keeps <c>schemas/workflow-definition.schema.json</c> and the <c>samples/workflows</c> files aligned
/// with what the SDK actually parses, and keeps the shared <c>nodeId</c> pattern identical to the agent
/// schema's.
/// </summary>
public class WorkflowSchemaTests
{
    private static string SchemaPath => Path.Combine(AppContext.BaseDirectory, "schemas", "workflow-definition.schema.json");

    private static string AgentSchemaPath => Path.Combine(AppContext.BaseDirectory, "schemas", "agent-definition.schema.json");

    private static string SamplePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "samples", "workflows", fileName);

    private static JsonObject Schema() => JsonNode.Parse(File.ReadAllText(SchemaPath))!.AsObject();

    [Fact]
    public void The_schema_is_valid_json_with_the_expected_shape()
    {
        var schema = Schema();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]!.GetValue<string>());
        Assert.Equal("object", schema["type"]!.GetValue<string>());

        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["id", "name", "nodes"], required);

        var properties = schema["properties"]!.AsObject().Select(property => property.Key).ToArray();
        Assert.Contains("id", properties);
        Assert.Contains("name", properties);
        Assert.Contains("description", properties);
        Assert.Contains("nodes", properties);
        Assert.Contains("connections", properties);
        Assert.Contains("metadata", properties);

        var defs = schema["$defs"]!.AsObject().Select(def => def.Key).ToArray();
        Assert.Contains("nodeId", defs);
        Assert.Contains("nodeKey", defs);
        Assert.Contains("nodeDefinition", defs);
        Assert.Contains("connection", defs);
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
    public void The_schema_node_id_pattern_agrees_with_NodeId(string candidate, bool expected)
    {
        var pattern = Schema()["$defs"]!["nodeId"]!["pattern"]!.GetValue<string>();

        Assert.Equal(expected, Regex.IsMatch(candidate, pattern));
        Assert.Equal(expected, NodeId.TryParse(candidate, out _));
    }

    [Fact]
    public void The_workflow_and_agent_schemas_share_the_node_id_pattern()
    {
        var workflowPattern = Schema()["$defs"]!["nodeId"]!["pattern"]!.GetValue<string>();
        var agentPattern = JsonNode.Parse(File.ReadAllText(AgentSchemaPath))!["$defs"]!["nodeId"]!["pattern"]!.GetValue<string>();

        Assert.Equal(agentPattern, workflowPattern);
    }

    [Theory]
    [InlineData("trigger", true)]
    [InlineData("triage-agent", true)]
    [InlineData("enrich_1", true)]
    [InlineData("A", true)]
    [InlineData("has space", false)]
    [InlineData("-leading", false)]
    [InlineData("under.dot", false)]
    [InlineData("", false)]
    public void The_node_key_pattern_agrees_with_WorkflowNodeDefinition(string candidate, bool expected)
    {
        var pattern = Schema()["$defs"]!["nodeKey"]!["pattern"]!.GetValue<string>();

        Assert.Equal(WorkflowNodeDefinition.KeyPattern, pattern);
        Assert.Equal(expected, Regex.IsMatch(candidate, pattern));
        Assert.Equal(expected, WorkflowNodeDefinition.IsValidKey(candidate));
    }

    [Fact]
    public void The_yaml_sample_loads_through_the_serializer()
    {
        var definition = WorkflowDefinitionSerializer.Load(SamplePath("support-flow.yaml")).Value;

        Assert.Equal(NodeId.Parse("acme/support-flow@1.0.0"), definition.Id);
        Assert.Equal("Support Flow", definition.Name);
        Assert.Equal(3, definition.Nodes.Count);
        Assert.Equal("scripts/notify.py", definition.Nodes[2].Config!["path"]!.GetValue<string>());

        var graph = definition.Validate().Value;
        Assert.Equal(["inbound"], graph.EntryNodes.Select(node => node.Key));
    }

    [Fact]
    public void The_json_sample_loads_through_the_serializer()
    {
        // The $schema property in the sample is tooling metadata; the loader ignores it.
        var definition = WorkflowDefinitionSerializer.Load(SamplePath("data-pipeline.json")).Value;

        Assert.Equal(NodeId.Parse("acme/data-pipeline@0.2.0"), definition.Id);
        Assert.Equal("Data Pipeline", definition.Name);
        Assert.Equal(5, definition.Nodes.Count);
        Assert.Equal(5, definition.Connections.Count);

        var graph = definition.Validate().Value;
        Assert.Equal(["every-morning"], graph.EntryNodes.Select(node => node.Key));
        Assert.Equal("load", graph.ExecutionOrder[^1]);
    }

    [Fact]
    public void Sample_ids_and_keys_match_the_schema_patterns()
    {
        var schema = Schema();
        var nodeIdPattern = schema["$defs"]!["nodeId"]!["pattern"]!.GetValue<string>();
        var nodeKeyPattern = schema["$defs"]!["nodeKey"]!["pattern"]!.GetValue<string>();

        foreach (var file in new[] { "support-flow.yaml", "data-pipeline.json" })
        {
            var definition = WorkflowDefinitionSerializer.Load(SamplePath(file)).Value;
            Assert.Matches(nodeIdPattern, definition.Id.ToString());

            foreach (var node in definition.Nodes)
            {
                Assert.Matches(nodeKeyPattern, node.Key);
                Assert.Matches(nodeIdPattern, node.Node.ToString());
            }
        }
    }
}
