using Ovi.Sdk.Operators;

namespace Ovi.Sdk.Triggers;

/// <summary>
/// The simplest trigger: starts a workflow on demand and passes the caller-supplied payload through
/// unchanged. Useful as the entry point for tests and for "run workflow now" tooling.
/// </summary>
public class ManualTriggerOperator<TPayload> : TriggerOperator<TPayload, TPayload>
{
    public ManualTriggerOperator(OperatorDescriptor? descriptor = null)
        : base(descriptor ?? DefaultDescriptor)
    {
    }

    public static OperatorDescriptor DefaultDescriptor { get; } = new(
        OperatorId.BuiltIn("manual-trigger"),
        "Manual Trigger",
        "Starts a workflow on demand with a caller-supplied payload.");

    public override ValueTask<TPayload> ExecuteAsync(TPayload input, WorkflowExecutionContext context) =>
        ValueTask.FromResult(input);
}
