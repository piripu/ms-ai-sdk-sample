namespace Ovi.Sdk.Operators;

/// <summary>
/// The strongly typed base class for workflow nodes. Deriving from this class is how operators
/// "define their own input and output": <typeparamref name="TInput"/> and
/// <typeparamref name="TOutput"/> are the operator's data contract with the rest of the workflow.
/// </summary>
/// <remarks>
/// Operators are atomic: an instance can be executed (and therefore unit tested) on its own by
/// calling <see cref="ExecuteAsync(TInput, WorkflowExecutionContext)"/> with a context built via
/// <see cref="WorkflowExecutionContext.CreateBuilder"/> — no workflow engine required.
/// </remarks>
/// <typeparam name="TInput">The input the operator consumes.</typeparam>
/// <typeparam name="TOutput">The output the operator produces.</typeparam>
public abstract class Operator<TInput, TOutput> : IOperator
{
    protected Operator(OperatorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    /// <inheritdoc />
    public OperatorDescriptor Descriptor { get; }

    /// <summary>The operator's identity (<c>org/name@version</c>).</summary>
    public OperatorId Id => Descriptor.Id;

    /// <summary>The operator's display name.</summary>
    public string Name => Descriptor.Name;

    /// <summary>The operator's description.</summary>
    public string? Description => Descriptor.Description;

    /// <inheritdoc />
    public Type InputType => typeof(TInput);

    /// <inheritdoc />
    public Type OutputType => typeof(TOutput);

    /// <summary>Executes the operator against the shared workflow execution context.</summary>
    public abstract ValueTask<TOutput> ExecuteAsync(TInput input, WorkflowExecutionContext context);

    async ValueTask<object?> IOperator.ExecuteAsync(object? input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var typedInput = input switch
        {
            TInput typed => typed,
            null when default(TInput) is null => default(TInput)!,
            null => throw new ArgumentNullException(
                nameof(input),
                $"Operator '{Id}' requires a non-null input of type {typeof(TInput)}."),
            _ => throw new ArgumentException(
                $"Operator '{Id}' expects input of type {typeof(TInput)}, but received {input.GetType()}.",
                nameof(input)),
        };

        return await ExecuteAsync(typedInput, context).ConfigureAwait(false);
    }
}
