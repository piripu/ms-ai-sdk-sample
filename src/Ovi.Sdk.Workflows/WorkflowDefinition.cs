using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Workflows;

/// <summary>
/// The declarative form of a workflow: its identity, the node instances it contains, and the
/// connections that wire them together — n8n-style, authorable in JSON or YAML (see
/// <see cref="WorkflowDefinitionSerializer"/>). This is a <b>definition</b>, not a running workflow:
/// it can be parsed, validated, and analyzed as a graph (<see cref="WorkflowGraph"/>) with no engine.
/// Executing it — binding node ids to <c>INode</c> instances, scheduling, passing data along edges —
/// is runtime work.
/// </summary>
public sealed record WorkflowDefinition
{
    public WorkflowDefinition()
    {
    }

    [SetsRequiredMembers]
    public WorkflowDefinition(
        NodeId id,
        string name,
        IReadOnlyList<WorkflowNodeDefinition> nodes,
        IReadOnlyList<WorkflowConnection>? connections = null,
        string? description = null,
        JsonObject? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(nodes);

        Id = id;
        Name = name;
        Nodes = nodes;
        Connections = connections ?? [];
        Description = description;
        Metadata = metadata;
    }

    /// <summary>The workflow's identity (<c>org/name@version</c>), from the shared node-id domain.</summary>
    public required NodeId Id { get; init; }

    /// <summary>The workflow's display name.</summary>
    public required string Name { get; init; }

    /// <summary>What the workflow does.</summary>
    public string? Description { get; init; }

    /// <summary>The node instances this workflow contains.</summary>
    public required IReadOnlyList<WorkflowNodeDefinition> Nodes { get; init; }

    /// <summary>The directed edges wiring the node instances together (empty for a single-node workflow).</summary>
    public IReadOnlyList<WorkflowConnection> Connections { get; init; } = [];

    /// <summary>Free-form editor metadata (canvas viewport, notes) — outside the SDK's semantics.</summary>
    public JsonObject? Metadata { get; init; }

    /// <summary>
    /// Validates the workflow as a graph and returns the analyzable <see cref="WorkflowGraph"/> on
    /// success, or a <see cref="ValidationError"/> describing the first structural problem
    /// (duplicate/blank key, dangling connection, self-loop, duplicate edge, or cycle).
    /// </summary>
    public Result<WorkflowGraph> Validate() => WorkflowGraph.Create(this);
}
