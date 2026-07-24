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
        var result = AgentDefinitionSerializer.FromYaml(Yaml);

        Assert.True(result.IsSuccess);
        var definition = result.Value;
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
        var fromYaml = AgentDefinitionSerializer.FromYaml(Yaml).Value;
        var fromJson = AgentDefinitionSerializer.FromJson(Json).Value;

        AssertEquivalent(fromYaml, fromJson);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var original = AgentDefinitionSerializer.FromYaml(Yaml).Value;
        var roundTripped = AgentDefinitionSerializer.FromJson(AgentDefinitionSerializer.ToJson(original)).Value;

        AssertEquivalent(original, roundTripped);
    }

    [Fact]
    public void Round_trips_through_yaml()
    {
        var original = AgentDefinitionSerializer.FromYaml(Yaml).Value;
        var roundTripped = AgentDefinitionSerializer.FromYaml(AgentDefinitionSerializer.ToYaml(original)).Value;

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

            AssertEquivalent(
                AgentDefinitionSerializer.Load(yamlPath).Value,
                AgentDefinitionSerializer.Load(jsonPath).Value);

            var unsupported = AgentDefinitionSerializer.Load(textPath);
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
        var result = AgentDefinitionSerializer.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-ovi.yaml"));

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ExecutionError>(result.Error);
        Assert.IsAssignableFrom<IOException>(error.Exception);
    }

    [Fact]
    public void Definitions_without_required_metadata_are_validation_errors()
    {
        var yaml = AgentDefinitionSerializer.FromYaml("id: acme/incomplete@1.0.0");
        Assert.True(yaml.IsFailure);
        var yamlError = Assert.IsType<ValidationError>(yaml.Error);
        Assert.NotNull(yamlError.Detail);

        var json = AgentDefinitionSerializer.FromJson("""{"name": "No Id"}""");
        Assert.True(json.IsFailure);
        Assert.IsType<ValidationError>(json.Error);

        Assert.True(AgentDefinitionSerializer.FromJson("   ").IsFailure);
        Assert.True(AgentDefinitionSerializer.FromYaml("   ").IsFailure);
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
