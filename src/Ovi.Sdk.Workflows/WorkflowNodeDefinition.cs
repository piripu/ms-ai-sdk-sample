using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Workflows;

/// <summary>
/// One node instance inside a workflow: a workflow-unique <see cref="Key"/> that connections refer
/// to, the node-type <see cref="Node"/> id it instantiates, and free-form <see cref="Config"/> for
/// the instance. This is the declarative counterpart of "a node placed on the canvas" in an n8n-style
/// editor — binding the id to a runnable <c>INode</c> is runtime work.
/// </summary>
public sealed partial record WorkflowNodeDefinition
{
    /// <summary>The anchored pattern a node <see cref="Key"/> must match (kept in lockstep with the JSON Schema's <c>nodeKey</c>).</summary>
    public const string KeyPattern = "^[A-Za-z0-9][A-Za-z0-9_-]*$";

    public WorkflowNodeDefinition()
    {
    }

    [SetsRequiredMembers]
    public WorkflowNodeDefinition(
        string key,
        NodeId node,
        JsonObject? config = null,
        string? displayName = null,
        JsonObject? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(node);

        Key = key;
        Node = node;
        Config = config;
        DisplayName = displayName;
        Metadata = metadata;
    }

    /// <summary>The workflow-unique instance key that <see cref="WorkflowConnection"/>s reference (slug-shaped; see <see cref="KeyPattern"/>).</summary>
    public required string Key { get; init; }

    /// <summary>The node-type identity this instance runs (<c>org/name@version</c> or <c>./name</c>).</summary>
    public required NodeId Node { get; init; }

    /// <summary>Free-form, instance-specific configuration passed to the node at bind time.</summary>
    public JsonObject? Config { get; init; }

    /// <summary>An optional display name for this instance (overrides the node type's name in editors).</summary>
    public string? DisplayName { get; init; }

    /// <summary>Free-form editor metadata (canvas position, notes) — outside the SDK's semantics.</summary>
    public JsonObject? Metadata { get; init; }

    /// <summary>Whether <paramref name="key"/> is a valid workflow node key (see <see cref="KeyPattern"/>).</summary>
    public static bool IsValidKey([NotNullWhen(true)] string? key) => key is not null && KeyRegex().IsMatch(key);

    [GeneratedRegex(KeyPattern)]
    private static partial Regex KeyRegex();
}
