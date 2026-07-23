using System.Diagnostics.CodeAnalysis;

namespace Ovi.Sdk.Nodes;

/// <summary>
/// The self-describing metadata every node carries: its domain <see cref="Id"/>, a human-readable
/// display <see cref="Name"/>, and an optional <see cref="Description"/>.
/// </summary>
public sealed record NodeDescriptor
{
    public NodeDescriptor()
    {
    }

    [SetsRequiredMembers]
    public NodeDescriptor(NodeId id, string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        Description = description;
    }

    /// <summary>The node's identity (<c>org/name@version</c> or <c>./name</c> for built-ins).</summary>
    public required NodeId Id { get; init; }

    /// <summary>The display name shown to humans (e.g. in a workflow editor).</summary>
    public required string Name { get; init; }

    /// <summary>What the node does, for humans and for LLM tool descriptions.</summary>
    public string? Description { get; init; }
}
