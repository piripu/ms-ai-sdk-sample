using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Triggers;

/// <summary>
/// The simplest trigger: starts a workflow on demand and passes the caller-supplied payload through
/// unchanged. Useful as the entry point for tests and for "run workflow now" tooling.
/// </summary>
public sealed class ManualTriggerNode<TPayload> : TriggerNode<TPayload, TPayload>
{
    public ManualTriggerNode(NodeDescriptor? descriptor = null)
        : base(descriptor ?? DefaultDescriptor)
    {
    }

    public static NodeDescriptor DefaultDescriptor { get; } = new(
        NodeId.BuiltIn("manual-trigger"),
        "Manual Trigger",
        "Starts a workflow on demand with a caller-supplied payload.");

    public override ValueTask<Result<TPayload>> ExecuteAsync(TPayload input, WorkflowExecutionContext context) =>
        ValueTask.FromResult(Result<TPayload>.Success(input));
}
