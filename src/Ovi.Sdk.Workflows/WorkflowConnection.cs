using System.Diagnostics.CodeAnalysis;

namespace Ovi.Sdk.Workflows;

/// <summary>
/// A directed edge between two node instances, by their <see cref="WorkflowNodeDefinition.Key"/>s:
/// output of <see cref="From"/> flows to input of <see cref="To"/>. Edges are single-output/single-input
/// for now — n8n-style output ports (an IF node's true/false branches) are a documented follow-up that
/// adds an optional port to this shape without changing existing definitions.
/// </summary>
public sealed record WorkflowConnection
{
    public WorkflowConnection()
    {
    }

    [SetsRequiredMembers]
    public WorkflowConnection(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        From = from;
        To = to;
    }

    /// <summary>The source node instance key (the edge's tail).</summary>
    public required string From { get; init; }

    /// <summary>The target node instance key (the edge's head).</summary>
    public required string To { get; init; }

    public override string ToString() => $"{From} -> {To}";
}
