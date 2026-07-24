using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ovi.Sdk.Internal;
using Ovi.Sdk.Nodes;
using YamlDotNet.Core;

namespace Ovi.Sdk.Workflows;

/// <summary>
/// Loads and saves <see cref="WorkflowDefinition"/>s as JSON or YAML. Both formats share one shape:
/// YAML documents are bridged onto the JSON object model first, so anything expressible in one format
/// behaves identically in the other.
/// </summary>
/// <remarks>
/// Loading parses the document and then validates it as a graph, so a returned definition is always
/// structurally sound (non-empty, uniquely keyed, acyclic). Failures are <see cref="Result{T}"/>
/// values: malformed or empty documents and graph-validation problems surface as
/// <see cref="ValidationError"/>, IO problems as <see cref="ExecutionError"/>. Writing
/// (<see cref="ToJson"/>/<see cref="ToYaml"/>) serializes an already-valid object and keeps throwing
/// on programmer error. Pass an <see cref="ILogger"/> to observe loads; omitted, logging is a no-op.
/// </remarks>
public static class WorkflowDefinitionSerializer
{
    public static Result<WorkflowDefinition> FromJson(string json, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        logger ??= NullLogger.Instance;

        if (string.IsNullOrWhiteSpace(json))
        {
            return new ValidationError("The JSON document is empty.");
        }

        try
        {
            var definition = JsonSerializer.Deserialize<WorkflowDefinition>(json, OviJson.DefaultOptions);
            if (definition is null)
            {
                return new ValidationError("The JSON document does not contain a workflow definition.");
            }

            return Validated(definition, "json", logger);
        }
        catch (JsonException exception)
        {
            WorkflowLog.ParseFailed(logger, "json", exception.Message);
            return new ValidationError("The JSON document is not a valid workflow definition.", exception.Message);
        }
    }

    public static Result<WorkflowDefinition> FromYaml(string yaml, ILogger? logger = null)
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

            var definition = node.Deserialize<WorkflowDefinition>(OviJson.DefaultOptions);
            if (definition is null)
            {
                return new ValidationError("The YAML document does not contain a workflow definition.");
            }

            return Validated(definition, "yaml", logger);
        }
        catch (Exception exception) when (exception is JsonException or YamlException or NotSupportedException)
        {
            WorkflowLog.ParseFailed(logger, "yaml", exception.Message);
            return new ValidationError("The YAML document is not a valid workflow definition.", exception.Message);
        }
    }

    /// <summary>Loads a definition from a <c>.json</c>, <c>.yaml</c> or <c>.yml</c> file.</summary>
    public static Result<WorkflowDefinition> Load(string path, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        logger ??= NullLogger.Instance;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".json" or ".yaml" or ".yml"))
        {
            return new ValidationError(
                $"'{extension}' is not a supported workflow definition format (expected .json, .yaml or .yml).",
                detail: path);
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WorkflowLog.ReadFailed(logger, exception, path);
            return ExecutionError.FromException(exception);
        }

        return extension is ".json" ? FromJson(content, logger) : FromYaml(content, logger);
    }

    public static string ToJson(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, OviJson.IndentedOptions);
    }

    public static string ToYaml(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var node = JsonSerializer.SerializeToNode(definition, OviJson.DefaultOptions)
            ?? throw new InvalidOperationException("The workflow definition serialized to nothing.");

        return YamlJsonBridge.ToYaml(node);
    }

    /// <summary>Runs graph validation on a parsed definition, logs the outcome, and shapes it as a result.</summary>
    private static Result<WorkflowDefinition> Validated(WorkflowDefinition definition, string format, ILogger logger)
    {
        var validation = definition.Validate();
        if (validation.IsFailure)
        {
            WorkflowLog.Invalid(logger, definition.Id, validation.Error.Message);
            return validation.Error;
        }

        WorkflowLog.Loaded(logger, definition.Id, format, definition.Nodes.Count);
        return definition;
    }
}

/// <summary>Source-generated log messages for workflow definition loading.</summary>
internal static partial class WorkflowLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Loaded workflow definition {NodeId} from {Format} ({NodeCount} nodes)")]
    public static partial void Loaded(ILogger logger, NodeId nodeId, string format, int nodeCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Workflow definition {Format} parse failed: {Detail}")]
    public static partial void ParseFailed(ILogger logger, string format, string detail);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Workflow definition file could not be read: {Path}")]
    public static partial void ReadFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Workflow definition {NodeId} is not a valid graph: {Detail}")]
    public static partial void Invalid(ILogger logger, NodeId nodeId, string detail);
}
