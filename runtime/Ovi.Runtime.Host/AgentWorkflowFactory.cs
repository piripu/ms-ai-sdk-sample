using Ovi.Sdk.Agents;
using Ovi.Sdk.Workflows;

namespace Ovi.Runtime.Host;

/// <summary>
/// How a bare agent runs: wrapped into a synthetic single-node workflow so
/// <see cref="WorkflowHost"/> only ever has one code path, whether it's hosting a full workflow or
/// just an agent. <see cref="WorkflowHostBuilder.UseAgent"/> is the entry point for this.
/// </summary>
public static class AgentWorkflowFactory
{
    /// <summary>The workflow instance key the wrapped agent node runs under.</summary>
    public const string EntryKey = "agent";

    /// <summary>
    /// Wraps <paramref name="definition"/> in a one-node <see cref="WorkflowDefinition"/>: a single
    /// instance (key <see cref="EntryKey"/>) whose node type is the agent's own id. No trigger node is
    /// needed — <see cref="WorkflowHost.RunAsync"/> is itself the entry point.
    /// </summary>
    public static WorkflowDefinition Wrap(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new WorkflowDefinition(
            definition.Id,
            definition.Name,
            nodes: [new WorkflowNodeDefinition(EntryKey, definition.Id)],
            description: definition.Description);
    }
}
