using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Scripting;

/// <summary>
/// A workflow node whose behavior is a Python script. The script defines
/// <c>def run(input, context)</c>; the node's input and output are JSON
/// (<see cref="JsonNode"/>), which keeps the node composable with any other node.
/// </summary>
/// <remarks>
/// Execution is delegated to an <see cref="IPythonScriptEngine"/>, resolved per execution: the
/// engine fixed on this node first, then one registered in
/// <see cref="WorkflowExecutionContext.RuntimeServices"/> — a missing engine is a
/// <see cref="ResolutionError"/> failure, an engine exception an <see cref="ExecutionError"/>.
/// The SDK ships the contract only — engine implementations arrive with the runtime (see
/// <c>docs/python-script-execution.md</c>), and tests use fake engines, keeping script nodes
/// atomically testable now.
/// </remarks>
public sealed class PythonScriptNode : Node<JsonNode?, JsonNode?>
{
    public PythonScriptNode(
        PythonScript script,
        NodeDescriptor? descriptor = null,
        IPythonScriptEngine? engine = null)
        : base(descriptor ?? DefaultDescriptor)
    {
        ArgumentNullException.ThrowIfNull(script);

        Script = script;
        Engine = engine;
    }

    public static NodeDescriptor DefaultDescriptor { get; } = new(
        NodeId.BuiltIn("python-script"),
        "Python Script",
        "Runs a Python script (def run(input, context)) as a workflow node.");

    /// <summary>The script this node runs.</summary>
    public PythonScript Script { get; }

    /// <summary>An engine fixed on this node; when null, the engine is resolved from the context.</summary>
    public IPythonScriptEngine? Engine { get; }

    public override async ValueTask<Result<JsonNode?>> ExecuteAsync(JsonNode? input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var logger = context.GetLogger<PythonScriptNode>();
        var engine = Engine ?? context.GetService<IPythonScriptEngine>();
        if (engine is null)
        {
            ScriptLog.NoEngine(logger, Id);
            return new ResolutionError(
                $"Script node '{Id}' has no Python engine.",
                hint: "Provide an IPythonScriptEngine on the node or register one in RuntimeServices; engine implementations are a runtime concern — see docs/python-script-execution.md.");
        }

        var request = new PythonScriptExecutionRequest(Script, input, PythonScriptContextSnapshot.Capture(context));
        ScriptLog.Executing(logger, Id, engine.GetType().Name, Script.EntryPoint);

        try
        {
            return await engine.ExecuteAsync(request, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ScriptLog.EngineFailed(logger, exception, Id);
            return ExecutionError.FromException(exception);
        }
    }
}

/// <summary>Source-generated log messages for script execution.</summary>
internal static partial class ScriptLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Executing script node {NodeId} via {EngineType} (entry point '{EntryPoint}')")]
    public static partial void Executing(ILogger logger, NodeId nodeId, string engineType, string entryPoint);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Script node {NodeId} has no Python engine")]
    public static partial void NoEngine(ILogger logger, NodeId nodeId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Script engine failed for node {NodeId}")]
    public static partial void EngineFailed(ILogger logger, Exception exception, NodeId nodeId);
}
