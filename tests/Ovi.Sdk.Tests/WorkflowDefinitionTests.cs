using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Sdk.Tests;

public class WorkflowDefinitionTests
{
    private const string Yaml = """
        id: acme/support-flow@1.0.0
        name: Support Flow
        description: Triage inbound chats and post a reply.
        nodes:
          - key: inbound
            node: ./chat-trigger
          - key: triage
            node: acme/support-triage@0.1.0
            displayName: Triage the message
          - key: notify
            node: ./python-script
            config:
              path: scripts/notify.py
              timeoutSeconds: 30
        connections:
          - from: inbound
            to: triage
          - from: triage
            to: notify
        metadata:
          note: reviewed
        """;

    private const string Json = """
        {
          "id": "acme/support-flow@1.0.0",
          "name": "Support Flow",
          "description": "Triage inbound chats and post a reply.",
          "nodes": [
            { "key": "inbound", "node": "./chat-trigger" },
            { "key": "triage", "node": "acme/support-triage@0.1.0", "displayName": "Triage the message" },
            { "key": "notify", "node": "./python-script", "config": { "path": "scripts/notify.py", "timeoutSeconds": 30 } }
          ],
          "connections": [
            { "from": "inbound", "to": "triage" },
            { "from": "triage", "to": "notify" }
          ],
          "metadata": { "note": "reviewed" }
        }
        """;

    [Fact]
    public void Loads_a_workflow_from_yaml()
    {
        var result = WorkflowDefinitionSerializer.FromYaml(Yaml);

        Assert.True(result.IsSuccess);
        var definition = result.Value;
        Assert.Equal(NodeId.Parse("acme/support-flow@1.0.0"), definition.Id);
        Assert.Equal("Support Flow", definition.Name);
        Assert.Equal("Triage inbound chats and post a reply.", definition.Description);

        Assert.Equal(3, definition.Nodes.Count);
        Assert.Equal("inbound", definition.Nodes[0].Key);
        Assert.True(definition.Nodes[0].Node.IsBuiltIn);
        Assert.Equal("chat-trigger", definition.Nodes[0].Node.Name);
        Assert.Equal(NodeId.Parse("acme/support-triage@0.1.0"), definition.Nodes[1].Node);
        Assert.Equal("Triage the message", definition.Nodes[1].DisplayName);

        Assert.Equal("scripts/notify.py", definition.Nodes[2].Config!["path"]!.GetValue<string>());
        Assert.Equal(30, definition.Nodes[2].Config!["timeoutSeconds"]!.GetValue<int>());

        Assert.Equal(2, definition.Connections.Count);
        Assert.Equal("inbound", definition.Connections[0].From);
        Assert.Equal("triage", definition.Connections[0].To);
        Assert.Equal("reviewed", definition.Metadata!["note"]!.GetValue<string>());
    }

    [Fact]
    public void Yaml_and_json_produce_the_same_definition()
    {
        var fromYaml = WorkflowDefinitionSerializer.FromYaml(Yaml).Value;
        var fromJson = WorkflowDefinitionSerializer.FromJson(Json).Value;

        AssertEquivalent(fromYaml, fromJson);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var original = WorkflowDefinitionSerializer.FromYaml(Yaml).Value;
        var roundTripped = WorkflowDefinitionSerializer.FromJson(WorkflowDefinitionSerializer.ToJson(original)).Value;

        AssertEquivalent(original, roundTripped);
    }

    [Fact]
    public void Round_trips_through_yaml()
    {
        var original = WorkflowDefinitionSerializer.FromYaml(Yaml).Value;
        var roundTripped = WorkflowDefinitionSerializer.FromYaml(WorkflowDefinitionSerializer.ToYaml(original)).Value;

        AssertEquivalent(original, roundTripped);
    }

    [Fact]
    public void Loads_definitions_from_files_by_extension()
    {
        var directory = Directory.CreateTempSubdirectory("ovi-workflow-tests-");
        try
        {
            var yamlPath = Path.Combine(directory.FullName, "flow.yaml");
            var jsonPath = Path.Combine(directory.FullName, "flow.json");
            var textPath = Path.Combine(directory.FullName, "flow.txt");
            File.WriteAllText(yamlPath, Yaml);
            File.WriteAllText(jsonPath, Json);
            File.WriteAllText(textPath, Yaml);

            AssertEquivalent(
                WorkflowDefinitionSerializer.Load(yamlPath).Value,
                WorkflowDefinitionSerializer.Load(jsonPath).Value);

            var unsupported = WorkflowDefinitionSerializer.Load(textPath);
            Assert.True(unsupported.IsFailure);
            var error = Assert.IsType<ValidationError>(unsupported.Error);
            Assert.Contains(".txt", error.Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Missing_files_are_execution_errors()
    {
        var result = WorkflowDefinitionSerializer.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-ovi-flow.yaml"));

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ExecutionError>(result.Error);
        Assert.IsAssignableFrom<IOException>(error.Exception);
    }

    [Fact]
    public void Definitions_without_required_metadata_are_validation_errors()
    {
        // Missing the required 'nodes' array.
        var noNodes = WorkflowDefinitionSerializer.FromJson("""{"id": "acme/x@1.0.0", "name": "X"}""");
        Assert.True(noNodes.IsFailure);
        Assert.IsType<ValidationError>(noNodes.Error);

        // Missing the required 'id'.
        var noId = WorkflowDefinitionSerializer.FromYaml("name: No Id\nnodes: []");
        Assert.True(noId.IsFailure);
        Assert.IsType<ValidationError>(noId.Error);

        Assert.True(WorkflowDefinitionSerializer.FromJson("   ").IsFailure);
        Assert.True(WorkflowDefinitionSerializer.FromYaml("   ").IsFailure);
    }

    [Fact]
    public void Malformed_documents_are_validation_errors_with_detail()
    {
        var badJson = WorkflowDefinitionSerializer.FromJson("{ not json");
        Assert.True(badJson.IsFailure);
        var error = Assert.IsType<ValidationError>(badJson.Error);
        Assert.NotNull(error.Detail);
    }

    [Fact]
    public void A_structurally_invalid_workflow_fails_to_load()
    {
        // Parses fine, but the connection dangles — the loader validates the graph before returning.
        const string dangling = """
            id: acme/broken@1.0.0
            name: Broken
            nodes:
              - key: a
                node: ./python-script
            connections:
              - from: a
                to: ghost
            """;

        var result = WorkflowDefinitionSerializer.FromYaml(dangling);
        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("ghost", error.Detail);
    }

    [Fact]
    public void A_node_id_that_is_not_valid_is_a_validation_error()
    {
        // 'acme/web-search' has no version, which is only allowed for built-ins.
        const string badNodeId = """
            id: acme/flow@1.0.0
            name: Flow
            nodes:
              - key: a
                node: acme/web-search
            """;

        var result = WorkflowDefinitionSerializer.FromYaml(badNodeId);
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
    }

    private static void AssertEquivalent(WorkflowDefinition expected, WorkflowDefinition actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Metadata?.ToJsonString(), actual.Metadata?.ToJsonString());

        Assert.Equal(expected.Nodes.Count, actual.Nodes.Count);
        for (var i = 0; i < expected.Nodes.Count; i++)
        {
            Assert.Equal(expected.Nodes[i].Key, actual.Nodes[i].Key);
            Assert.Equal(expected.Nodes[i].Node, actual.Nodes[i].Node);
            Assert.Equal(expected.Nodes[i].DisplayName, actual.Nodes[i].DisplayName);
            Assert.Equal(expected.Nodes[i].Config?.ToJsonString(), actual.Nodes[i].Config?.ToJsonString());
            Assert.Equal(expected.Nodes[i].Metadata?.ToJsonString(), actual.Nodes[i].Metadata?.ToJsonString());
        }

        Assert.Equal(expected.Connections.Count, actual.Connections.Count);
        for (var i = 0; i < expected.Connections.Count; i++)
        {
            Assert.Equal(expected.Connections[i].From, actual.Connections[i].From);
            Assert.Equal(expected.Connections[i].To, actual.Connections[i].To);
        }
    }
}
