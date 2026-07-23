using Microsoft.Extensions.AI;
using Ovi.Sdk.Operators;

namespace Ovi.Sdk.Agents;

/// <summary>
/// A <see cref="WorkflowExecutionContext"/> specialized for agent execution. On top of the shared
/// context it carries the current run's <see cref="ChatClient"/> and the mutable
/// <see cref="ChatHistory"/> agents read from and append to.
/// </summary>
/// <remarks>
/// Built by wrapping an existing base context, so state stores, services, cancellation and the
/// properties bag remain shared with the rest of the run. Future agent-scoped capabilities (e.g.
/// memory) belong here as well.
/// </remarks>
public class AgentWorkflowExecutionContext : WorkflowExecutionContext
{
    public AgentWorkflowExecutionContext(
        WorkflowExecutionContext baseContext,
        IChatClient chatClient,
        IList<ChatMessage>? chatHistory = null)
        : base(baseContext)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        ChatClient = chatClient;
        ChatHistory = chatHistory ?? [];
    }

    /// <summary>The chat client agents in this run converse through.</summary>
    public IChatClient ChatClient { get; }

    /// <summary>
    /// The conversation so far. Agents prepend this history to their requests and append both the
    /// incoming request messages and the model's response messages after each turn.
    /// </summary>
    public IList<ChatMessage> ChatHistory { get; }
}
