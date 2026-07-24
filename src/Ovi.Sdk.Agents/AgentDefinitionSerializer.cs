using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ovi.Sdk.Internal;
using Ovi.Sdk.Nodes;
using YamlDotNet.Core;

namespace Ovi.Sdk.Agents;

/// <summary>
/// Loads and saves <see cref="AgentDefinition"/>s as JSON or YAML. Both formats share one shape:
/// YAML documents are bridged onto the JSON object model first, so anything expressible in one format
/// behaves identically in the other.
/// </summary>
/// <remarks>
/// Loading returns a <see cref="Result{T}"/>: malformed or empty documents surface as
/// <see cref="ValidationError"/> failures (with parser detail), IO problems as
/// <see cref="ExecutionError"/> failures. Writing (<see cref="ToJson"/>/<see cref="ToYaml"/>)
/// serializes an already-valid object and keeps throwing on programmer error. Pass an
/// <see cref="ILogger"/> to observe loads; omitted, logging is a no-op.
/// </remarks>
public static class AgentDefinitionSerializer
{
    public static Result<AgentDefinition> FromJson(string json, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        logger ??= NullLogger.Instance;

        if (string.IsNullOrWhiteSpace(json))
        {
            return new ValidationError("The JSON document is empty.");
        }

        try
        {
            var definition = JsonSerializer.Deserialize<AgentDefinition>(json, OviJson.DefaultOptions);
            if (definition is null)
            {
                return new ValidationError("The JSON document does not contain an agent definition.");
            }

            SerializerLog.Loaded(logger, definition.Id, "json");
            return definition;
        }
        catch (JsonException exception)
        {
            SerializerLog.ParseFailed(logger, "json", exception.Message);
            return new ValidationError("The JSON document is not a valid agent definition.", exception.Message);
        }
    }

    public static Result<AgentDefinition> FromYaml(string yaml, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        logger ??= NullLogger.Instance;

        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new ValidationError("The YAML document is empty.");
        }

        try
        {
            var node = YamlJsonBridge.ToJsonNode(yaml);
            if (node is null)
            {
                return new ValidationError("The YAML document is empty.");
            }

            var definition = node.Deserialize<AgentDefinition>(OviJson.DefaultOptions);
            if (definition is null)
            {
                return new ValidationError("The YAML document does not contain an agent definition.");
            }

            SerializerLog.Loaded(logger, definition.Id, "yaml");
            return definition;
        }
        catch (Exception exception) when (exception is JsonException or YamlException or NotSupportedException)
        {
            SerializerLog.ParseFailed(logger, "yaml", exception.Message);
            return new ValidationError("The YAML document is not a valid agent definition.", exception.Message);
        }
    }

    /// <summary>Loads a definition from a <c>.json</c>, <c>.yaml</c> or <c>.yml</c> file.</summary>
    public static Result<AgentDefinition> Load(string path, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        logger ??= NullLogger.Instance;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".json" or ".yaml" or ".yml"))
        {
            return new ValidationError(
                $"'{extension}' is not a supported agent definition format (expected .json, .yaml or .yml).",
                detail: path);
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SerializerLog.ReadFailed(logger, exception, path);
            return ExecutionError.FromException(exception);
        }

        return extension is ".json" ? FromJson(content, logger) : FromYaml(content, logger);
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

/// <summary>Source-generated log messages for definition loading.</summary>
internal static partial class SerializerLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Loaded agent definition {NodeId} from {Format}")]
    public static partial void Loaded(ILogger logger, NodeId nodeId, string format);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Agent definition {Format} parse failed: {Detail}")]
    public static partial void ParseFailed(ILogger logger, string format, string detail);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Agent definition file could not be read: {Path}")]
    public static partial void ReadFailed(ILogger logger, Exception exception, string path);
}
