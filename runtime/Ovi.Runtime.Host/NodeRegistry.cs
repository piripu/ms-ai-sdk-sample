using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host;

/// <summary>
/// Binds the node-type <see cref="NodeId"/>s a <see cref="Ovi.Sdk.Workflows.WorkflowDefinition"/>
/// references to runnable <see cref="INode"/> instances. <c>Ovi.Sdk.Workflows</c> deliberately stops
/// at the definition — "binding ids to instances... is runtime work" (see
/// <c>docs/architecture.md</c>) — this is that binding.
/// </summary>
public interface INodeRegistry
{
    /// <summary>Resolves the node instance for <paramref name="nodeId"/>, or returns <see langword="false"/> when none is registered.</summary>
    bool TryResolve(NodeId nodeId, out INode? node);
}

/// <summary>The default <see cref="INodeRegistry"/>: an in-memory map populated by <see cref="WorkflowHostBuilder.MapNode(INode)"/>.</summary>
public sealed class NodeRegistry : INodeRegistry
{
    private readonly Dictionary<NodeId, INode> _nodes = [];

    /// <summary>Registers <paramref name="node"/> under its own <c>Descriptor.Id</c>.</summary>
    public NodeRegistry Register(INode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Register(node.Descriptor.Id, node);
    }

    /// <summary>Registers <paramref name="node"/> under an explicit <paramref name="nodeId"/> (useful when a workflow references it by an alias).</summary>
    public NodeRegistry Register(NodeId nodeId, INode node)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(node);
        _nodes[nodeId] = node;
        return this;
    }

    /// <inheritdoc />
    public bool TryResolve(NodeId nodeId, out INode? node)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        if (_nodes.TryGetValue(nodeId, out node))
        {
            return true;
        }

        // Fall back to a version-insensitive match (NodeId.IsSameNode), so a built-in registered as
        // "./name" still resolves a workflow reference to "./name@1.0.0" and vice versa.
        foreach (var (registeredId, registeredNode) in _nodes)
        {
            if (registeredId.IsSameNode(nodeId))
            {
                node = registeredNode;
                return true;
            }
        }

        node = null;
        return false;
    }
}
