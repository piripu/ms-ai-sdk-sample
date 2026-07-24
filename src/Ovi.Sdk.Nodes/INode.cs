namespace Ovi.Sdk.Nodes;

/// <summary>
/// The untyped view of a workflow node. A node is the atomic unit of a workflow: it declares its
/// identity via <see cref="Descriptor"/>, the shape of the data it consumes and produces via
/// <see cref="InputType"/>/<see cref="OutputType"/>, and executes against a shared
/// <see cref="WorkflowExecutionContext"/>.
/// </summary>
/// <remarks>
/// Implementations should derive from <see cref="Node{TInput, TOutput}"/> rather than implementing
/// this interface directly. The untyped surface exists so a workflow runtime can wire heterogeneous
/// nodes together; the typed surface is what makes a node individually testable.
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
    /// </summary>
    ValueTask<object?> ExecuteAsync(object? input, WorkflowExecutionContext context);
}
