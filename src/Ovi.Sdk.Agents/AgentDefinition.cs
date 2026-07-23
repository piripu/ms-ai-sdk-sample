using System.Diagnostics.CodeAnalysis;
using Ovi.Sdk.Operators;
using Ovi.Sdk.Tools;

namespace Ovi.Sdk.Agents;

/// <summary>
/// The declarative form of an agent: operator identity metadata (id, display name, description) plus
/// the agent's instructions and the tools it may call. Definitions can be authored in JSON or YAML —
/// see <see cref="AgentDefinitionSerializer"/> — and turned into a runnable operator with
/// <see cref="AgentOperator.FromDefinition"/>.
/// </summary>
public sealed record AgentDefinition
{
    public AgentDefinition()
    {
    }

    [SetsRequiredMembers]
    public AgentDefinition(
        OperatorId id,
        string name,
        string? description = null,
        string? instructions = null,
        IReadOnlyList<ToolReference>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        Description = description;
        Instructions = instructions;
        Tools = tools ?? [];
    }

    /// <summary>The agent's identity (<c>org/name@version</c>).</summary>
    public required OperatorId Id { get; init; }

    /// <summary>The agent's display name.</summary>
    public required string Name { get; init; }

    /// <summary>What the agent is for.</summary>
    public string? Description { get; init; }

    /// <summary>The system instructions that steer the agent.</summary>
    public string? Instructions { get; init; }

    /// <summary>References to the tools the agent may call (resolved via an <see cref="IToolCatalog"/>).</summary>
    public IReadOnlyList<ToolReference> Tools { get; init; } = [];

    /// <summary>The operator descriptor slice of this definition.</summary>
    public OperatorDescriptor ToDescriptor() => new(Id, Name, Description);
}
