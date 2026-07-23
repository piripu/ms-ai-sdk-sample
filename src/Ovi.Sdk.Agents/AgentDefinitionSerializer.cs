using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ovi.Sdk.Agents;

/// <summary>
/// Loads and saves <see cref="AgentDefinition"/>s as JSON or YAML. Both formats share one shape:
/// YAML documents are bridged onto the JSON object model first, so anything expressible in one format
/// behaves identically in the other.
/// </summary>
public static class AgentDefinitionSerializer
{
    public static AgentDefinition FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<AgentDefinition>(json, OviJson.DefaultOptions)
            ?? throw new InvalidOperationException("The JSON document does not contain an agent definition.");
    }

    public static AgentDefinition FromYaml(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var node = YamlJsonBridge.ToJsonNode(yaml)
            ?? throw new InvalidOperationException("The YAML document is empty.");

        return node.Deserialize<AgentDefinition>(OviJson.DefaultOptions)
            ?? throw new InvalidOperationException("The YAML document does not contain an agent definition.");
    }

    /// <summary>Loads a definition from a <c>.json</c>, <c>.yaml</c> or <c>.yml</c> file.</summary>
    public static AgentDefinition Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var content = File.ReadAllText(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => FromJson(content),
            ".yaml" or ".yml" => FromYaml(content),
            var extension => throw new NotSupportedException(
                $"'{extension}' is not a supported agent definition format (expected .json, .yaml or .yml)."),
        };
    }

    public static string ToJson(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, OviJson.IndentedOptions);
    }

    public static string ToYaml(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var node = JsonSerializer.SerializeToNode(definition, OviJson.DefaultOptions)
            ?? throw new InvalidOperationException("The agent definition serialized to nothing.");

        return YamlJsonBridge.ToYaml(node);
    }
}
