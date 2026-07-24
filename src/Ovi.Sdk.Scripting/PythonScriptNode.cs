using System.Text.Json.Nodes;
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
/// <see cref="WorkflowExecutionContext.RuntimeServices"/>. The SDK ships the contract only — engine
/// implementations arrive with the runtime (see <c>docs/python-script-execution.md</c> for the
/// execution plan), and tests use fake engines, keeping script nodes atomically testable now.
/// </remarks>
public class PythonScriptNode : Node<JsonNode?, JsonNode?>
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

    public override async ValueTask<JsonNode?> ExecuteAsync(JsonNode? input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var engine = Engine
            ?? context.GetService<IPythonScriptEngine>()
            ?? throw new InvalidOperationException(
                $"Script node '{Id}' has no Python engine. Provide an IPythonScriptEngine on the node or register one in RuntimeServices; engine implementations are a runtime concern — see docs/python-script-execution.md.");

        var request = new PythonScriptExecutionRequest(Script, input, PythonScriptContextSnapshot.Capture(context));
        return await engine.ExecuteAsync(request, context.CancellationToken).ConfigureAwait(false);
    }
}
