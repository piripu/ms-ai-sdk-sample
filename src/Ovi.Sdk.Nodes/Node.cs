namespace Ovi.Sdk.Nodes;

/// <summary>
/// The strongly typed base class for workflow nodes. Deriving from this class is how nodes
/// "define their own input and output": <typeparamref name="TInput"/> and
/// <typeparamref name="TOutput"/> are the node's data contract with the rest of the workflow.
/// </summary>
/// <remarks>
/// Nodes are atomic: an instance can be executed (and therefore unit tested) on its own by
/// calling <see cref="ExecuteAsync(TInput, WorkflowExecutionContext)"/> with a context built via
/// <see cref="WorkflowExecutionContext.CreateBuilder"/> — no workflow engine required.
/// </remarks>
/// <typeparam name="TInput">The input the node consumes.</typeparam>
/// <typeparam name="TOutput">The output the node produces.</typeparam>
public abstract class Node<TInput, TOutput> : INode
{
    protected Node(NodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    /// <inheritdoc />
    public NodeDescriptor Descriptor { get; }

    /// <summary>The node's identity (<c>org/name@version</c>).</summary>
    public NodeId Id => Descriptor.Id;

    /// <summary>The node's display name.</summary>
    public string Name => Descriptor.Name;

    /// <summary>The node's description.</summary>
    public string? Description => Descriptor.Description;

    /// <inheritdoc />
    public Type InputType => typeof(TInput);

    /// <inheritdoc />
    public Type OutputType => typeof(TOutput);

    /// <summary>Executes the node against the shared workflow execution context.</summary>
    public abstract ValueTask<TOutput> ExecuteAsync(TInput input, WorkflowExecutionContext context);

    async ValueTask<object?> INode.ExecuteAsync(object? input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var typedInput = input switch
        {
            TInput typed => typed,
            null when default(TInput) is null => default(TInput)!,
            null => throw new ArgumentNullException(
                nameof(input),
                $"Node '{Id}' requires a non-null input of type {typeof(TInput)}."),
            _ => throw new ArgumentException(
                $"Node '{Id}' expects input of type {typeof(TInput)}, but received {input.GetType()}.",
                nameof(input)),
        };

        return await ExecuteAsync(typedInput, context).ConfigureAwait(false);
    }
}
