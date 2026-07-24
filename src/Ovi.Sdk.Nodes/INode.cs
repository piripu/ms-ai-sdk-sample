namespace Ovi.Sdk.Nodes;

/// <summary>
/// The untyped view of a workflow node. A node is the atomic unit of a workflow: it declares its
/// identity via <see cref="Descriptor"/>, the shape of the data it consumes and produces via
/// <see cref="InputType"/>/<see cref="OutputType"/>, and executes against a shared
/// <see cref="WorkflowExecutionContext"/>.
/// </summary>
/// <remarks>
/// The untyped surface exists so a workflow runtime can wire heterogeneous nodes together; the typed
/// contract is <see cref="INode{TInput, TOutput}"/>. Most nodes are composed via
/// <see cref="Node.Create{TInput, TOutput}(NodeDescriptor, Func{TInput, WorkflowExecutionContext, TOutput})"/>
/// or use the optional <see cref="Node{TInput, TOutput}"/> base class. Execution reports expected
/// failures as a failed <see cref="Result{T}"/> rather than by throwing; see <see cref="Error"/>.
/// </remarks>
public interface INode
{
    /// <summary>The node's identity and display metadata.</summary>
    NodeDescriptor Descriptor { get; }

    /// <summary>The CLR type this node accepts as input.</summary>
    Type InputType { get; }

    /// <summary>The CLR type this node produces as output.</summary>
    Type OutputType { get; }

    /// <summary>
    /// Executes the node with an untyped input, which must be assignable to <see cref="InputType"/>.
    /// A mismatched input yields a failed result (<see cref="ValidationError"/>) rather than a throw.
    /// </summary>
    ValueTask<Result<object?>> ExecuteAsync(object? input, WorkflowExecutionContext context);
}

/// <summary>
/// The strongly typed contract of a workflow node: <typeparamref name="TInput"/> and
/// <typeparamref name="TOutput"/> are the node's data contract with the rest of the workflow.
/// This is the surface to depend on — for decorating nodes with cross-cutting behavior (retry,
/// logging, timeouts) and for executing them atomically in tests.
/// </summary>
/// <typeparam name="TInput">The input the node consumes.</typeparam>
/// <typeparam name="TOutput">The output the node produces.</typeparam>
public interface INode<TInput, TOutput> : INode
{
    /// <summary>
    /// Executes the node against the shared workflow execution context. Expected failures surface
    /// as a failed <see cref="Result{T}"/> carrying an <see cref="Error"/>; exceptions are reserved
    /// for programmer errors and cancellation.
    /// </summary>
    ValueTask<Result<TOutput>> ExecuteAsync(TInput input, WorkflowExecutionContext context);
}
