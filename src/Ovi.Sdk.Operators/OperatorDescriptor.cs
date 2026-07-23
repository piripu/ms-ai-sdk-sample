using System.Diagnostics.CodeAnalysis;

namespace Ovi.Sdk.Operators;

/// <summary>
/// The self-describing metadata every operator carries: its domain <see cref="Id"/>, a human-readable
/// display <see cref="Name"/>, and an optional <see cref="Description"/>.
/// </summary>
public sealed record OperatorDescriptor
{
    public OperatorDescriptor()
    {
    }

    [SetsRequiredMembers]
    public OperatorDescriptor(OperatorId id, string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        Description = description;
    }

    /// <summary>The operator's identity (<c>org/name@version</c> or <c>./name</c> for built-ins).</summary>
    public required OperatorId Id { get; init; }

    /// <summary>The display name shown to humans (e.g. in a workflow editor).</summary>
    public required string Name { get; init; }

    /// <summary>What the operator does, for humans and for LLM tool descriptions.</summary>
    public string? Description { get; init; }
}
