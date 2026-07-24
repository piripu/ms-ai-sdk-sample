using Microsoft.Extensions.AI;

namespace Ovi.Sdk.Agents;

/// <summary>
/// The input an <see cref="AgentNode"/> consumes: either a plain user <see cref="Prompt"/>, a
/// list of chat <see cref="Messages"/>, or both (messages first, prompt appended as a user turn).
/// </summary>
public sealed record AgentRequest
{
    /// <summary>A plain-text user prompt.</summary>
    public string? Prompt { get; init; }

    /// <summary>Fully formed chat messages to send.</summary>
    public IReadOnlyList<ChatMessage>? Messages { get; init; }

    public static implicit operator AgentRequest(string prompt) => new() { Prompt = prompt };
}

/// <summary>
/// The output an <see cref="AgentNode"/> produces. <see cref="Text"/> is the assistant's reply;
/// <see cref="RawResponse"/> exposes the underlying Microsoft.Extensions.AI response when richer
/// data (usage, tool calls, additional messages) is needed.
/// </summary>
public sealed record AgentResponse
{
    public AgentResponse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public AgentResponse(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Text = response.Text;
        RawResponse = response;
    }

    /// <summary>The assistant's textual reply.</summary>
    public string Text { get; }

    /// <summary>The full underlying chat response, when the agent ran against a chat client.</summary>
    public ChatResponse? RawResponse { get; }
}
