using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host;

/// <summary>
/// Carries one node's input/output and identity while <see cref="WorkflowHost"/> walks the workflow
/// graph — the per-node counterpart of <see cref="RunIoFeature"/>. Re-attached (overwriting the
/// previous node's) before each node executes; safe because a single run walks nodes sequentially.
/// </summary>
public sealed class NodeIoFeature
{
    public NodeIoFeature(INode node, string key, object? input)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Node = node;
        Key = key;
        Input = input;
    }

    /// <summary>The node instance being executed.</summary>
    public INode Node { get; }

    /// <summary>The node's workflow instance key (<c>WorkflowNodeDefinition.Key</c>).</summary>
    public string Key { get; }

    /// <summary>The value passed to the node.</summary>
    public object? Input { get; set; }

    /// <summary>The node's result. Set by the innermost stage — the actual <c>INode.ExecuteAsync</c> call.</summary>
    public Result<object?>? Output { get; set; }
}
