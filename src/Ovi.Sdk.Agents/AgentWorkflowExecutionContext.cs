using Microsoft.Extensions.AI;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Agents;

/// <summary>
/// Convenience sugar for agent execution: wraps a base <see cref="WorkflowExecutionContext"/> and
/// attaches an <see cref="AgentChatFeature"/> carrying the run's <see cref="ChatClient"/> and
/// <see cref="ChatHistory"/>.
/// </summary>
/// <remarks>
/// The capability lives in the feature, not the subtype: agent nodes read
/// <c>context.GetFeature&lt;AgentChatFeature&gt;()</c>, so a plain context with the feature attached
/// (via <c>SetFeature</c> or the builder's <c>WithFeature</c>) behaves identically to this class.
/// State stores, services, cancellation, properties and features remain shared with the wrapped
/// context.
/// </remarks>
public sealed class AgentWorkflowExecutionContext : WorkflowExecutionContext
{
    private readonly AgentChatFeature _chat;

    public AgentWorkflowExecutionContext(
        WorkflowExecutionContext baseContext,
        IChatClient chatClient,
        IList<ChatMessage>? chatHistory = null)
        : base(baseContext)
    {
        _chat = new AgentChatFeature(chatClient, chatHistory);
        SetFeature(_chat);
    }

    /// <summary>The chat client agents in this run converse through.</summary>
    public IChatClient ChatClient => _chat.ChatClient;

    /// <summary>The conversation so far (see <see cref="AgentChatFeature.ChatHistory"/>).</summary>
    public IList<ChatMessage> ChatHistory => _chat.ChatHistory;
}
