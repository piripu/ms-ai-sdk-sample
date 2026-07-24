using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Triggers;

/// <summary>Marks a node as a workflow starting point.</summary>
public interface ITriggerNode : INode
{
}

/// <summary>
/// The base class for workflow starting points. A trigger is an ordinary node whose input is the
/// external event payload delivered by the runtime (a webhook request, a schedule tick, …) and whose
/// output is what flows into the rest of the workflow.
/// </summary>
/// <remarks>
/// Every trigger can be fired manually — for testing, or for "run now" in an editor — by calling
/// <see cref="FireAsync"/> with a hand-crafted payload. No runtime infrastructure is required.
/// </remarks>
/// <typeparam name="TPayload">The external event payload that fires the trigger.</typeparam>
/// <typeparam name="TOutput">The output the trigger emits into the workflow.</typeparam>
public abstract class TriggerNode<TPayload, TOutput> : Node<TPayload, TOutput>, ITriggerNode
{
    protected TriggerNode(NodeDescriptor descriptor)
        : base(descriptor)
    {
    }

    /// <summary>
    /// Fires the trigger by hand with the given payload. Semantically identical to
    /// <see cref="Node{TPayload, TOutput}.ExecuteAsync(TPayload, WorkflowExecutionContext)"/>;
    /// exists to make manual/test firing explicit at call sites.
    /// </summary>
    public ValueTask<TOutput> FireAsync(TPayload payload, WorkflowExecutionContext context) =>
        ExecuteAsync(payload, context);
}
