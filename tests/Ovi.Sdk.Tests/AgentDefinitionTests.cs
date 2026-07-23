using System.Text.Json;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Nodes;
using Xunit;

namespace Ovi.Sdk.Tests;

public class AgentDefinitionTests
{
    private const string Yaml = """
        id: acme/researcher@1.0.0
        name: Researcher
        description: Answers questions using its tools.
        instructions: |
          You are a careful research assistant.
          Cite your sources.
        tools:
          - ./echo
          - id: acme/web-search@2.0.0
            options:
              maxResults: 5
        """;

    private const string Json = """
        {
          "id": "acme/researcher@1.0.0",
          "name": "Researcher",
          "description": "Answers questions using its tools.",
          "instructions": "You are a careful research assistant.\nCite your sources.\n",
          "tools": [
            "./echo",
            { "id": "acme/web-search@2.0.0", "options": { "maxResults": 5 } }
          ]
        }
        """;

    [Fact]
    public void Loads_an_agent_from_yaml()
    {
        var definition = AgentDefinitionSerializer.FromYaml(Yaml);

        Assert.Equal(NodeId.Parse("acme/researcher@1.0.0"), definition.Id);
        Assert.Equal("Researcher", definition.Name);
        Assert.Equal("Answers questions using its tools.", definition.Description);
        Assert.Contains("careful research assistant", definition.Instructions);

        Assert.Equal(2, definition.Tools.Count);
        Assert.True(definition.Tools[0].Id.IsBuiltIn);
        Assert.Equal("echo", definition.Tools[0].Id.Name);
        Assert.Equal(NodeId.Parse("acme/web-search@2.0.0"), definition.Tools[1].Id);
        Assert.Equal(5, definition.Tools[1].Options!["maxResults"]!.GetValue<int>());
    }

    [Fact]
    public void Yaml_and_json_produce_the_same_definition()
    {
        var fromYaml = AgentDefinitionSerializer.FromYaml(Yaml);
        var fromJson = AgentDefinitionSerializer.FromJson(Json);

        AssertEquivalent(fromYaml, fromJson);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var original = AgentDefinitionSerializer.FromYaml(Yaml);
        var roundTripped = AgentDefinitionSerializer.FromJson(AgentDefinitionSerializer.ToJson(original));

        AssertEquivalent(original, roundTripped);
    }

    [Fact]
    public void Round_trips_through_yaml()
    {
        var original = AgentDefinitionSerializer.FromYaml(Yaml);
        var roundTripped = AgentDefinitionSerializer.FromYaml(AgentDefinitionSerializer.ToYaml(original));

        AssertEquivalent(original, roundTripped);
    }

    [Fact]
    public void Loads_definitions_from_files_by_extension()
    {
        var directory = Directory.CreateTempSubdirectory("ovi-agent-tests-");
        try
        {
            var yamlPath = Path.Combine(directory.FullName, "agent.yaml");
            var jsonPath = Path.Combine(directory.FullName, "agent.json");
            var textPath = Path.Combine(directory.FullName, "agent.txt");
            File.WriteAllText(yamlPath, Yaml);
            File.WriteAllText(jsonPath, Json);
            File.WriteAllText(textPath, Yaml);

            AssertEquivalent(AgentDefinitionSerializer.Load(yamlPath), AgentDefinitionSerializer.Load(jsonPath));
            Assert.Throws<NotSupportedException>(() => AgentDefinitionSerializer.Load(textPath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Definitions_without_required_metadata_are_rejected()
    {
        Assert.ThrowsAny<JsonException>(() => AgentDefinitionSerializer.FromYaml("id: acme/incomplete@1.0.0"));
        Assert.ThrowsAny<JsonException>(() => AgentDefinitionSerializer.FromJson("""{"name": "No Id"}"""));
    }

    private static void AssertEquivalent(AgentDefinition expected, AgentDefinition actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Instructions, actual.Instructions);
        Assert.Equal(expected.Tools.Count, actual.Tools.Count);

        for (var i = 0; i < expected.Tools.Count; i++)
        {
            Assert.Equal(expected.Tools[i].Id, actual.Tools[i].Id);
            Assert.Equal(expected.Tools[i].Options?.ToJsonString(), actual.Tools[i].Options?.ToJsonString());
        }
    }
}
