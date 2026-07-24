using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Workflows;

/// <summary>
/// A validated, analyzable view of a <see cref="WorkflowDefinition"/>'s connection graph. Obtained
/// through <see cref="Create"/> (or <see cref="WorkflowDefinition.Validate"/>), which enforces the
/// v1 structural rules — a non-empty, uniquely-keyed set of node instances wired into a directed
/// acyclic graph — and precomputes entry nodes and a stable topological execution order.
/// </summary>
/// <remarks>
/// Workflows are DAGs in v1: cycles are rejected. n8n-style loop constructs are a runtime concern and
/// a documented follow-up. "Entry nodes" are simply nodes with no inbound edges; whether an entry node
/// is a trigger is a property of its node type, not of the workflow shape.
/// </remarks>
public sealed class WorkflowGraph
{
    private readonly Dictionary<string, WorkflowNodeDefinition> _nodesByKey;
    private readonly Dictionary<string, IReadOnlyList<string>> _downstream;
    private readonly Dictionary<string, IReadOnlyList<string>> _upstream;

    private WorkflowGraph(
        WorkflowDefinition definition,
        Dictionary<string, WorkflowNodeDefinition> nodesByKey,
        Dictionary<string, IReadOnlyList<string>> downstream,
        Dictionary<string, IReadOnlyList<string>> upstream,
        IReadOnlyList<WorkflowNodeDefinition> entryNodes,
        IReadOnlyList<string> executionOrder)
    {
        Definition = definition;
        _nodesByKey = nodesByKey;
        _downstream = downstream;
        _upstream = upstream;
        EntryNodes = entryNodes;
        ExecutionOrder = executionOrder;
    }

    /// <summary>The definition this graph was built from.</summary>
    public WorkflowDefinition Definition { get; }

    /// <summary>The node instances with no inbound connections, in definition order — where a run begins.</summary>
    public IReadOnlyList<WorkflowNodeDefinition> EntryNodes { get; }

    /// <summary>
    /// A stable topological ordering of node keys: every node appears after all of its upstream
    /// nodes. Ties are broken by definition order, so the ordering is deterministic for a given
    /// definition.
    /// </summary>
    public IReadOnlyList<string> ExecutionOrder { get; }

    /// <summary>Looks up a node instance by its key.</summary>
    public bool TryGetNode(string key, out WorkflowNodeDefinition? node)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _nodesByKey.TryGetValue(key, out node);
    }

    /// <summary>The keys directly downstream of <paramref name="key"/> (its immediate successors), in definition order.</summary>
    public IReadOnlyList<string> GetDownstream(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _downstream.TryGetValue(key, out var targets) ? targets : [];
    }

    /// <summary>The keys directly upstream of <paramref name="key"/> (its immediate predecessors), in definition order.</summary>
    public IReadOnlyList<string> GetUpstream(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _upstream.TryGetValue(key, out var sources) ? sources : [];
    }

    /// <summary>
    /// Validates <paramref name="definition"/> and builds its graph. Structural problems come back as
    /// <see cref="ValidationError"/> failures (never exceptions): an empty workflow, a blank or
    /// non-slug key, a duplicate key, a connection to an unknown key, a self-loop, a duplicate edge,
    /// or a cycle (the error names the nodes that could not be ordered).
    /// </summary>
    public static Result<WorkflowGraph> Create(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Nodes.Count == 0)
        {
            return new ValidationError("A workflow must contain at least one node.");
        }

        var nodesByKey = new Dictionary<string, WorkflowNodeDefinition>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            if (!WorkflowNodeDefinition.IsValidKey(node.Key))
            {
                return new ValidationError(
                    $"Node key '{node.Key}' is not valid; keys must match {WorkflowNodeDefinition.KeyPattern}.",
                    detail: node.Key);
            }

            if (!nodesByKey.TryAdd(node.Key, node))
            {
                return new ValidationError($"Duplicate node key '{node.Key}'; keys must be unique within a workflow.", detail: node.Key);
            }
        }

        var downstream = definition.Nodes.ToDictionary(node => node.Key, _ => new List<string>(), StringComparer.Ordinal);
        var upstream = definition.Nodes.ToDictionary(node => node.Key, _ => new List<string>(), StringComparer.Ordinal);
        var edges = new HashSet<(string From, string To)>();

        foreach (var connection in definition.Connections)
        {
            if (!nodesByKey.ContainsKey(connection.From))
            {
                return new ValidationError(
                    $"Connection '{connection}' starts at unknown node key '{connection.From}'.",
                    detail: connection.From);
            }

            if (!nodesByKey.ContainsKey(connection.To))
            {
                return new ValidationError(
                    $"Connection '{connection}' ends at unknown node key '{connection.To}'.",
                    detail: connection.To);
            }

            if (string.Equals(connection.From, connection.To, StringComparison.Ordinal))
            {
                return new ValidationError($"Node '{connection.From}' connects to itself; self-loops are not allowed.", detail: connection.From);
            }

            if (!edges.Add((connection.From, connection.To)))
            {
                return new ValidationError($"Duplicate connection '{connection}'.", detail: connection.ToString());
            }

            downstream[connection.From].Add(connection.To);
            upstream[connection.To].Add(connection.From);
        }

        var order = TopologicalSort(definition, downstream, upstream);
        if (order.Count != definition.Nodes.Count)
        {
            var emitted = new HashSet<string>(order, StringComparer.Ordinal);
            var stuck = definition.Nodes.Select(node => node.Key).Where(key => !emitted.Contains(key)).ToArray();
            return new ValidationError(
                $"The workflow has a cycle; these nodes could not be ordered: {string.Join(", ", stuck)}. Workflows must be acyclic.",
                detail: string.Join(",", stuck));
        }

        var entryNodes = definition.Nodes.Where(node => upstream[node.Key].Count == 0).ToArray();
        var readOnlyDownstream = downstream.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
        var readOnlyUpstream = upstream.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);

        return new WorkflowGraph(definition, nodesByKey, readOnlyDownstream, readOnlyUpstream, entryNodes, order);
    }

    /// <summary>
    /// Kahn's algorithm, tie-broken by definition order for a stable result. Returns a complete
    /// ordering when the graph is acyclic; otherwise returns a partial ordering whose missing keys
    /// are exactly the nodes a cycle prevented from being scheduled.
    /// </summary>
    private static List<string> TopologicalSort(
        WorkflowDefinition definition,
        Dictionary<string, List<string>> downstream,
        Dictionary<string, List<string>> upstream)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < definition.Nodes.Count; i++)
        {
            index[definition.Nodes[i].Key] = i;
        }

        var inDegree = definition.Nodes.ToDictionary(node => node.Key, node => upstream[node.Key].Count, StringComparer.Ordinal);
        var ready = new SortedSet<int>(definition.Nodes.Where(node => inDegree[node.Key] == 0).Select(node => index[node.Key]));

        var order = new List<string>(definition.Nodes.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);

            var key = definition.Nodes[next].Key;
            order.Add(key);

            foreach (var target in downstream[key])
            {
                if (--inDegree[target] == 0)
                {
                    ready.Add(index[target]);
                }
            }
        }

        return order;
    }
}
