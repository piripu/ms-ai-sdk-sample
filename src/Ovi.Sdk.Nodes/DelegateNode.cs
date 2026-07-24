namespace Ovi.Sdk.Nodes;

/// <summary>
/// Composes workflow nodes from delegates — behavior as data, no subclassing:
/// <c>Node.Create(descriptor, (input, context) =&gt; …)</c>.
/// </summary>
public static class Node
{
    /// <summary>Creates a node from an asynchronous delegate.</summary>
    public static DelegateNode<TInput, TOutput> Create<TInput, TOutput>(
        NodeDescriptor descriptor,
        Func<TInput, WorkflowExecutionContext, ValueTask<TOutput>> execute) =>
        new(descriptor, execute);

    /// <summary>Creates a node from a synchronous delegate.</summary>
    public static DelegateNode<TInput, TOutput> Create<TInput, TOutput>(
        NodeDescriptor descriptor,
        Func<TInput, WorkflowExecutionContext, TOutput> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return new DelegateNode<TInput, TOutput>(
            descriptor,
            (input, context) => ValueTask.FromResult(execute(input, context)));
    }
}

/// <summary>
/// A node whose behavior is a composed delegate. Useful for inline nodes in tests and workflows,
/// and as the building block for decorators that wrap an <see cref="INode{TInput, TOutput}"/> with
/// cross-cutting behavior.
/// </summary>
public sealed class DelegateNode<TInput, TOutput> : Node<TInput, TOutput>
{
    private readonly Func<TInput, WorkflowExecutionContext, ValueTask<TOutput>> _execute;

    public DelegateNode(
        NodeDescriptor descriptor,
        Func<TInput, WorkflowExecutionContext, ValueTask<TOutput>> execute)
        : base(descriptor)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
    }

    public override ValueTask<TOutput> ExecuteAsync(TInput input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _execute(input, context);
    }
}
