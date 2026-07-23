namespace Ovi.Sdk.Operators;

/// <summary>
/// The untyped view of a workflow node. An operator is the atomic unit of a workflow: it declares its
/// identity via <see cref="Descriptor"/>, the shape of the data it consumes and produces via
/// <see cref="InputType"/>/<see cref="OutputType"/>, and executes against a shared
/// <see cref="WorkflowExecutionContext"/>.
/// </summary>
/// <remarks>
/// Implementations should derive from <see cref="Operator{TInput, TOutput}"/> rather than implementing
/// this interface directly. The untyped surface exists so a workflow runtime can wire heterogeneous
/// nodes together; the typed surface is what makes an operator individually testable.
/// </remarks>
public interface IOperator
{
    /// <summary>The operator's identity and display metadata.</summary>
    OperatorDescriptor Descriptor { get; }

    /// <summary>The CLR type this operator accepts as input.</summary>
    Type InputType { get; }

    /// <summary>The CLR type this operator produces as output.</summary>
    Type OutputType { get; }

    /// <summary>
    /// Executes the operator with an untyped input, which must be assignable to <see cref="InputType"/>.
    /// </summary>
    ValueTask<object?> ExecuteAsync(object? input, WorkflowExecutionContext context);
}
