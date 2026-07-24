namespace Ovi.Sdk.Nodes;

/// <summary>
/// The optional convenience base class for workflow nodes: implements the untyped
/// <see cref="INode"/> bridge and identity plumbing so implementations only supply
/// <see cref="ExecuteAsync(TInput, WorkflowExecutionContext)"/>. The contract to depend on is
/// <see cref="INode{TInput, TOutput}"/>; nodes can equally be composed from a delegate via
/// <see cref="Node.Create{TInput, TOutput}(NodeDescriptor, Func{TInput, WorkflowExecutionContext, TOutput})"/>.
/// </summary>
/// <remarks>
/// Nodes are atomic: an instance can be executed (and therefore unit tested) on its own by
/// calling <see cref="ExecuteAsync(TInput, WorkflowExecutionContext)"/> with a context built via
/// <see cref="WorkflowExecutionContext.CreateBuilder"/> — no workflow engine required.
/// The untyped bridge converts mismatched inputs to <see cref="ValidationError"/> failures and
/// unhandled exceptions to <see cref="ExecutionError"/> failures (logged through the context), so
/// a runtime driving heterogeneous nodes never has to guard the call with try/catch.
/// </remarks>
/// <typeparam name="TInput">The input the node consumes.</typeparam>
/// <typeparam name="TOutput">The output the node produces.</typeparam>
public abstract class Node<TInput, TOutput> : INode<TInput, TOutput>
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

    /// <inheritdoc />
    public abstract ValueTask<Result<TOutput>> ExecuteAsync(TInput input, WorkflowExecutionContext context);

    async ValueTask<Result<object?>> INode.ExecuteAsync(object? input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TInput typedInput;
        switch (input)
        {
            case TInput typed:
                typedInput = typed;
                break;
            case null when default(TInput) is null:
                typedInput = default!;
                break;
            case null:
                return new ValidationError(
                    $"Node '{Id}' requires a non-null input of type {typeof(TInput)}.");
            default:
                return new ValidationError(
                    $"Node '{Id}' expects input of type {typeof(TInput)}, but received {input.GetType()}.");
        }

        try
        {
            var result = await ExecuteAsync(typedInput, context).ConfigureAwait(false);
            return result.IsSuccess
                ? Result<object?>.Success(result.Value)
                : Result<object?>.Failure(result.Error);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation stays exceptional — it is not a node failure
        }
        catch (Exception exception)
        {
            NodeLog.UnhandledException(context.GetLogger(GetType().FullName ?? nameof(Node)), exception, Id);
            return ExecutionError.FromException(exception);
        }
    }
}
