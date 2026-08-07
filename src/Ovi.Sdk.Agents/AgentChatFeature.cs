using Microsoft.Extensions.AI;

namespace Ovi.Sdk.Agents;

/// <summary>
/// The conversational capability of a run, attached to a <see cref="Ovi.Sdk.Nodes.WorkflowExecutionContext"/>
/// as a typed feature (<c>context.SetFeature(new AgentChatFeature(client))</c>). Agent nodes read
/// this feature — no context subclassing required — so chat composes freely with other run
/// capabilities (future memory being the obvious next one).
/// </summary>
public sealed class AgentChatFeature
{
    public AgentChatFeature(IChatClient chatClient, IList<ChatMessage>? chatHistory = null)
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

    /// <summary>The most recent message in <see cref="ChatHistory"/>, or <see langword="null"/> when it's empty.</summary>
    public ChatMessage? LastMessage => ChatHistory.Count > 0 ? ChatHistory[^1] : null;
}
