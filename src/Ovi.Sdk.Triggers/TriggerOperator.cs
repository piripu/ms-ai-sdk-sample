using Ovi.Sdk.Operators;

namespace Ovi.Sdk.Triggers;

/// <summary>Marks an operator as a workflow starting point.</summary>
public interface ITriggerOperator : IOperator
{
}

/// <summary>
/// The base class for workflow starting points. A trigger is an ordinary operator whose input is the
/// external event payload delivered by the runtime (a webhook request, a schedule tick, …) and whose
/// output is what flows into the rest of the workflow.
/// </summary>
/// <remarks>
/// Every trigger can be fired manually — for testing, or for "run now" in an editor — by calling
/// <see cref="FireAsync"/> with a hand-crafted payload. No runtime infrastructure is required.
/// </remarks>
/// <typeparam name="TPayload">The external event payload that fires the trigger.</typeparam>
/// <typeparam name="TOutput">The output the trigger emits into the workflow.</typeparam>
public abstract class TriggerOperator<TPayload, TOutput> : Operator<TPayload, TOutput>, ITriggerOperator
{
    protected TriggerOperator(OperatorDescriptor descriptor)
        : base(descriptor)
    {
    }

    /// <summary>
    /// Fires the trigger by hand with the given payload. Semantically identical to
    /// <see cref="Operator{TPayload, TOutput}.ExecuteAsync(TPayload, WorkflowExecutionContext)"/>;
    /// exists to make manual/test firing explicit at call sites.
    /// </summary>
    public ValueTask<TOutput> FireAsync(TPayload payload, WorkflowExecutionContext context) =>
        ExecuteAsync(payload, context);
}
