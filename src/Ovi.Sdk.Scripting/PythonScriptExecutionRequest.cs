using System.Text.Json;
using System.Text.Json.Nodes;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Scripting;

/// <summary>
/// The serializable snapshot of the execution context a script sees as its <c>context</c> argument:
/// workflow/run information plus the three state scopes, all exported to JSON. Read-only by design —
/// scripts observe state; writing back is a future capability (see docs/python-script-execution.md).
/// </summary>
public sealed record PythonScriptContextSnapshot
{
    public required WorkflowInfo Workflow { get; init; }

    public required JsonObject WorkflowState { get; init; }

    public required JsonObject GlobalState { get; init; }

    public required JsonObject InstanceState { get; init; }

    /// <summary>Captures the serializable slice of a live execution context.</summary>
    public static PythonScriptContextSnapshot Capture(WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new PythonScriptContextSnapshot
        {
            Workflow = context.Workflow,
            WorkflowState = context.WorkflowState.ToJsonObject(),
            GlobalState = context.GlobalState.ToJsonObject(),
            InstanceState = context.InstanceState.ToJsonObject(),
        };
    }

    /// <summary>
    /// The snapshot as one JSON object (camelCase) — the exact value an engine decodes into the
    /// Python <c>context</c> dict.
    /// </summary>
    public JsonObject ToJsonObject() =>
        JsonSerializer.SerializeToNode(this, OviJson.DefaultOptions)!.AsObject();
}

/// <summary>Everything an <see cref="IPythonScriptEngine"/> needs to run one script invocation.</summary>
public sealed record PythonScriptExecutionRequest
{
    public PythonScriptExecutionRequest()
    {
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PythonScriptExecutionRequest(PythonScript script, JsonNode? input, PythonScriptContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        Script = script;
        Input = input;
        Context = context;
    }

    public required PythonScript Script { get; init; }

    /// <summary>The node input, handed to the script's entry point as <c>input</c>.</summary>
    public JsonNode? Input { get; init; }

    /// <summary>The context snapshot, handed to the script's entry point as <c>context</c>.</summary>
    public required PythonScriptContextSnapshot Context { get; init; }
}
